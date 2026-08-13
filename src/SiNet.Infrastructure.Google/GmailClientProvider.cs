using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Owns the shared Google <b>user</b> OAuth session for the new stack (Gmail + Drive + Sheets). One
/// <see cref="UserCredential"/> (with automatic refresh) backs <see cref="GmailService"/>,
/// <see cref="DriveService"/>, and <see cref="SheetsService"/>. Windows and feature surfaces must
/// not sign in again per operation — they consume this singleton via
/// <see cref="IConnectorAuthService"/> / the typed TryGet* APIs.
/// <para>
/// Auth strategy: try a <b>silent</b> restore from the token store first (no browser); refresh a
/// stale token using the refresh token only. Interactive consent is attempted only when
/// <see cref="GmailOptions.AllowInteractiveSignIn"/> is set (or via explicit
/// <see cref="SignInInteractiveAsync"/>). Expanding scopes (e.g. Drive) may require a one-time
/// interactive re-consent; silent restore still succeeds for previously granted scopes.
/// </para>
/// </summary>
public sealed class GmailClientProvider : IAsyncDisposable
{
    // Gmail read/send/modify + Drive (ProjectWork/Reports) + Spreadsheets (MasterPlan R0x).
    // Expanding scopes invalidates authorization for newly added capabilities until the user
    // re-consents interactively.
    private static readonly string[] Scopes =
    {
        GmailService.Scope.GmailReadonly,
        GmailService.Scope.GmailSend,
        GmailService.Scope.GmailModify,
        DriveService.Scope.Drive,
        SheetsService.Scope.Spreadsheets,
    };

    private const string TokenUser = "user";

    private readonly GmailOptions _options;
    private readonly IAppLogger _logger;
    private readonly IGoogleClientSecretsPathProvider? _pathProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UserCredential? _credential;
    private GmailService? _gmailService;
    private DriveService? _driveService;
    private SheetsService? _sheetsService;

    public GmailClientProvider(
        GmailOptions options,
        IAppLogger logger,
        IGoogleClientSecretsPathProvider? pathProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathProvider = pathProvider;
    }

    /// <summary>The OAuth scopes requested for the shared user session (Gmail + Drive + Sheets).</summary>
    internal static IReadOnlyList<string> RequestedScopes => Scopes;

    /// <summary>The root Gmail label under which projects are filed.</summary>
    public string RootLabel => _options.RootLabel;

    /// <summary>Default Gmail query for general mailbox paging (legacy: label:INBOX).</summary>
    public string DefaultMailboxQuery => _options.DefaultMailboxQuery;

    /// <summary>
    /// <c>true</c> once a usable Google user session has been established and cached. Reflects the
    /// last known state for the UI; it does not itself attempt a sign-in.
    /// </summary>
    public bool IsSignedIn => _credential != null;

    /// <summary>
    /// Raised whenever the signed-in state transitions. The payload is the new
    /// <see cref="IsSignedIn"/> value. Handlers must not throw.
    /// </summary>
    public event Action<bool>? AuthStateChanged;

    private void RaiseIfAuthStateChanged(bool wasSignedIn)
    {
        var isSignedIn = _credential != null;
        if (isSignedIn != wasSignedIn)
        {
            AuthStateChanged?.Invoke(isSignedIn);
        }
    }

    /// <summary>
    /// Returns a ready <see cref="GmailService"/> built from the shared credential, or <c>null</c>
    /// when the mailbox is not available.
    /// </summary>
    public async Task<GmailService?> TryGetServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_gmailService != null)
            return _gmailService;

        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = await EnsureCredentialLockedAsync(allowInteractive: false, cancellationToken)
                .ConfigureAwait(false);
            return credential == null ? null : EnsureGmailServiceLocked(credential);
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Returns a ready <see cref="DriveService"/> built from the <b>same</b> shared credential as
    /// Gmail, or <c>null</c> when the user session is not available. Does not open a browser.
    /// </summary>
    public async Task<DriveService?> TryGetDriveServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService != null)
            return _driveService;

        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = await EnsureCredentialLockedAsync(allowInteractive: false, cancellationToken)
                .ConfigureAwait(false);
            return credential == null ? null : EnsureDriveServiceLocked(credential);
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Returns a ready <see cref="SheetsService"/> from the shared credential, or <c>null</c>
    /// when the user session is not available. Does not open a browser.
    /// </summary>
    public async Task<SheetsService?> TryGetSheetsServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_sheetsService != null)
            return _sheetsService;

        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = await EnsureCredentialLockedAsync(allowInteractive: false, cancellationToken)
                .ConfigureAwait(false);
            return credential == null ? null : EnsureSheetsServiceLocked(credential);
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Attempts a <b>silent</b> sign-in from the persisted token store only (never opens a
    /// browser). Suitable for application startup.
    /// </summary>
    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken = default)
    {
        if (_credential != null)
            return true;

        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = await EnsureCredentialLockedAsync(allowInteractive: false, cancellationToken)
                .ConfigureAwait(false);
            return credential != null;
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Performs an <b>explicit, user-initiated</b> sign-in. Tries a silent restore first; if none
    /// is available it opens the browser for OAuth consent regardless of
    /// <see cref="GmailOptions.AllowInteractiveSignIn"/>.
    /// </summary>
    public Task<GmailSignInResult> SignInInteractiveAsync(CancellationToken cancellationToken = default) =>
        SignInInteractiveAsync(options: null, cancellationToken);

    /// <summary>
    /// Performs an <b>explicit, user-initiated</b> sign-in. By default tries silent restore first;
    /// when <paramref name="options"/>.<see cref="ConnectorLoginOptions.SkipSilentRestore"/> is
    /// <c>true</c>, opens the browser immediately.
    /// </summary>
    public async Task<GmailSignInResult> SignInInteractiveAsync(
        ConnectorLoginOptions? options,
        CancellationToken cancellationToken = default)
    {
        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_credential != null && options?.SkipSilentRestore != true)
                return GmailSignInResult.Success;

            ClearCachedServicesLocked();

            var setup = await TryPrepareAuthAsync(cancellationToken).ConfigureAwait(false);
            if (setup == null)
                return GmailSignInResult.NotConfigured;

            var (secrets, dataStore) = setup.Value;

            UserCredential? credential = null;
            if (options?.SkipSilentRestore != true)
            {
                credential = await TryRestoreAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false);
            }

            credential ??= await AuthorizeInteractiveAsync(
                secrets,
                dataStore,
                promptAccountSelection: options?.PromptAccountSelection == true,
                cancellationToken).ConfigureAwait(false);

            if (credential == null)
                return GmailSignInResult.Failed;

            BindCredentialLocked(credential);
            return GmailSignInResult.Success;
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Signs out: disposes cached clients and deletes the persisted refresh token directory.
    /// Prefer <see cref="LogoutAsync"/> from async call sites.
    /// </summary>
    public void Logout() => LogoutAsync().GetAwaiter().GetResult();

    /// <summary>Async sign-out for the shared Google user session (Gmail + Drive together).</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var wasSignedIn = _credential != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearCachedServicesLocked();
            DeletePersistedTokenStore();
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    internal static void DeleteTokenStoreDirectory(string? tokenStorePath)
    {
        var tokenPath = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(tokenStorePath) ? "sinet-google-token" : tokenStorePath);

        try
        {
            if (Directory.Exists(tokenPath))
            {
                Directory.Delete(tokenPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort; callers must not throw to UI.
        }
    }

    private void DeletePersistedTokenStore()
    {
        try
        {
            DeleteTokenStoreDirectory(_options.TokenStorePath);
            _logger.Info("[Google] Persisted token store deleted on logout.");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Google] Failed to delete token store on logout: {ex.Message}");
        }
    }

    private async Task<UserCredential?> EnsureCredentialLockedAsync(bool allowInteractive, CancellationToken cancellationToken)
    {
        if (_credential != null)
            return _credential;

        var credential = await AcquireCredentialAsync(allowInteractive, cancellationToken).ConfigureAwait(false);
        if (credential == null)
            return null;

        BindCredentialLocked(credential);
        return _credential;
    }

    private void BindCredentialLocked(UserCredential credential)
    {
        _credential = credential;
        _gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
    }

    private GmailService EnsureGmailServiceLocked(UserCredential credential)
    {
        if (_gmailService != null)
            return _gmailService;

        _gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        return _gmailService;
    }

    private DriveService EnsureDriveServiceLocked(UserCredential credential)
    {
        if (_driveService != null)
            return _driveService;

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        return _driveService;
    }

    private SheetsService EnsureSheetsServiceLocked(UserCredential credential)
    {
        if (_sheetsService != null)
            return _sheetsService;

        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        return _sheetsService;
    }

    private void ClearCachedServicesLocked()
    {
        _gmailService?.Dispose();
        _gmailService = null;
        _driveService?.Dispose();
        _driveService = null;
        _sheetsService?.Dispose();
        _sheetsService = null;
        _credential = null;
    }

    private async Task<UserCredential?> AcquireCredentialAsync(bool allowInteractive, CancellationToken cancellationToken)
    {
        var setup = await TryPrepareAuthAsync(cancellationToken).ConfigureAwait(false);
        if (setup == null)
            return null;

        var (secrets, dataStore) = setup.Value;

        var restored = await TryRestoreAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false);
        if (restored != null)
            return restored;

        if (!allowInteractive || !_options.AllowInteractiveSignIn)
        {
            _logger.Warn("[Google] No stored token and interactive sign-in is disabled. Session unavailable.");
            return null;
        }

        return await AuthorizeInteractiveAsync(secrets, dataStore, promptAccountSelection: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(ClientSecrets Secrets, IDataStore DataStore)?> TryPrepareAuthAsync(
        CancellationToken cancellationToken)
    {
        var secretsPath = await ResolveSecretsPathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secretsPath) || !File.Exists(secretsPath))
        {
            _logger.Warn($"[Google] client secrets not found at '{secretsPath ?? "(null)"}'. Session unavailable.");
            return null;
        }

        var tokenPath = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(_options.TokenStorePath) ? "sinet-google-token" : _options.TokenStorePath);

        ClientSecrets secrets;
        try
        {
            await using var stream = new FileStream(secretsPath, FileMode.Open, FileAccess.Read);
            secrets = (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken).ConfigureAwait(false)).Secrets;
        }
        catch (Exception ex)
        {
            _logger.Error($"[Google] Failed to read client secrets: {ex.Message}", ex);
            return null;
        }

        return (secrets, new SerializedDataStore(new FileDataStore(tokenPath, fullPath: true)));
    }

    private async Task<string?> ResolveSecretsPathAsync(CancellationToken cancellationToken)
    {
        if (_pathProvider is not null)
        {
            var vaultPath = await _pathProvider.ResolveClientSecretsPathAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(vaultPath) && File.Exists(vaultPath))
            {
                return vaultPath;
            }

            _logger.Warn("[Google] Vault Google client secrets unavailable; config fallback is deprecated — use Secret Setup.");
        }

        var configuredPath = _options.ClientSecretsPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        if (File.Exists(configuredPath))
            return configuredPath;

        return null;
    }

    private async Task<UserCredential?> AuthorizeInteractiveAsync(
        ClientSecrets secrets,
        IDataStore dataStore,
        bool promptAccountSelection,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!promptAccountSelection)
            {
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    Scopes,
                    TokenUser,
                    cancellationToken,
                    dataStore).ConfigureAwait(false);

                _logger.Info("[Google] Interactive sign-in completed (Gmail + Drive scopes).");
                return credential;
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = Scopes,
                DataStore = dataStore,
            });

            var receiver = new LocalServerCodeReceiver();
            var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(receiver.RedirectUri);
            request.Prompt = "select_account";

            var response = await receiver.ReceiveCodeAsync(request, cancellationToken).ConfigureAwait(false);
            var token = await flow.ExchangeCodeForTokenAsync(
                TokenUser,
                response.Code,
                receiver.RedirectUri,
                cancellationToken).ConfigureAwait(false);

            _logger.Info("[Google] Interactive sign-in completed (account selection prompted).");
            return new UserCredential(flow, TokenUser, token);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Google] Interactive sign-in failed: {ex.Message}", ex);
            return null;
        }
    }

    private async Task<UserCredential?> TryRestoreAsync(
        ClientSecrets secrets,
        IDataStore dataStore,
        CancellationToken cancellationToken)
    {
        try
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = Scopes,
                DataStore = dataStore,
            });

            var token = await flow.LoadTokenAsync(TokenUser, cancellationToken).ConfigureAwait(false);
            if (token == null)
                return null;

            var credential = new UserCredential(flow, TokenUser, token);

            if (token.IsStale)
            {
                var refreshed = await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!refreshed)
                {
                    _logger.Warn("[Google] Stored token is stale and could not be refreshed.");
                    return null;
                }
            }

            _logger.Info("[Google] Silently restored session from token store.");
            return credential;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Google] Silent token restore failed: {ex.Message}");
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        var wasSignedIn = _credential != null;
        ClearCachedServicesLocked();
        _gate.Dispose();
        RaiseIfAuthStateChanged(wasSignedIn);
        return ValueTask.CompletedTask;
    }
}
