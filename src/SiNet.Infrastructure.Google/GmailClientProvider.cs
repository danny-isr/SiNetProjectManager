using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Owns a fresh, independent Gmail OAuth session for the new stack (its own client secrets and
/// token store, separate from the legacy <c>GoogleService</c>). Builds and caches a read-only
/// <see cref="GmailService"/> on first use.
/// <para>
/// Auth strategy: try a <b>silent</b> restore from the token store first (no browser); refresh a
/// stale token using the refresh token only. Interactive consent is attempted only when
/// <see cref="GmailOptions.AllowInteractiveSignIn"/> is set. When no usable credential exists,
/// <see cref="TryGetServiceAsync"/> returns <c>null</c> so callers can degrade gracefully.
/// </para>
/// </summary>
public sealed class GmailClientProvider : IAsyncDisposable
{
    // Read + send. GmailSend is the narrowest scope that allows sending (not full MailGoogleCom).
    // NOTE: expanding scopes invalidates the *send* authorization of any token persisted before
    // this change. Silent restore still works for reads; sending will surface as "requires consent"
    // until the user performs a deliberate interactive sign-in that re-grants read + send.
    private static readonly string[] Scopes =
    {
        GmailService.Scope.GmailReadonly,
        GmailService.Scope.GmailSend,
        GmailService.Scope.GmailModify,
    };
    private const string TokenUser = "user";

    private readonly GmailOptions _options;
    private readonly IAppLogger _logger;
    private readonly IGoogleClientSecretsPathProvider? _pathProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GmailService? _gmailService;

    public GmailClientProvider(
        GmailOptions options,
        IAppLogger logger,
        IGoogleClientSecretsPathProvider? pathProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathProvider = pathProvider;
    }

    /// <summary>The root Gmail label under which projects are filed.</summary>
    public string RootLabel => _options.RootLabel;

    /// <summary>Default Gmail query for general mailbox paging (legacy: label:INBOX).</summary>
    public string DefaultMailboxQuery => _options.DefaultMailboxQuery;

    /// <summary>
    /// <c>true</c> once a usable Gmail session has been established (via silent restore or an
    /// explicit interactive sign-in) and cached. Reflects the last known state for the UI; it does
    /// not itself attempt a sign-in. Call <see cref="TrySignInSilentlyAsync"/> or
    /// <see cref="SignInInteractiveAsync"/> to establish a session.
    /// </summary>
    public bool IsSignedIn => _gmailService != null;

    /// <summary>
    /// Raised whenever the signed-in state transitions (false→true on a successful silent restore
    /// or interactive sign-in; true→false on <see cref="Logout"/> or disposal). The payload is the
    /// new <see cref="IsSignedIn"/> value. This is the native equivalent of the legacy
    /// <c>GoogleService.AuthStateChanged</c> health/auth bridge. Handlers must not throw.
    /// </summary>
    public event Action<bool>? AuthStateChanged;

    /// <summary>
    /// Compares the cached-session state captured before a mutation (<paramref name="wasSignedIn"/>)
    /// with the current state and raises <see cref="AuthStateChanged"/> only on a real transition.
    /// Always call this outside the <c>_gate</c> so handlers cannot deadlock the provider.
    /// </summary>
    private void RaiseIfAuthStateChanged(bool wasSignedIn)
    {
        var isSignedIn = _gmailService != null;
        if (isSignedIn != wasSignedIn)
        {
            AuthStateChanged?.Invoke(isSignedIn);
        }
    }

    /// <summary>
    /// Returns a ready <see cref="GmailService"/>, or <c>null</c> when the mailbox is not
    /// available (no client secrets configured, or no token and interactive sign-in disabled).
    /// Never throws for the "not signed in" case.
    /// </summary>
    public async Task<GmailService?> TryGetServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_gmailService != null)
        {
            return _gmailService;
        }

        var wasSignedIn = _gmailService != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildServiceLockedAsync(allowInteractive: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Attempts a <b>silent</b> sign-in from the persisted token store only (never opens a
    /// browser). Suitable for application startup. Returns <c>true</c> when a usable session was
    /// restored and the cached service is ready. Never throws for the "not signed in" case.
    /// </summary>
    public async Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken = default)
    {
        if (_gmailService != null)
        {
            return true;
        }

        var wasSignedIn = _gmailService != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var service = await BuildServiceLockedAsync(allowInteractive: false, cancellationToken).ConfigureAwait(false);
            return service != null;
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
    /// <see cref="GmailOptions.AllowInteractiveSignIn"/> (which only guards <i>implicit</i> prompts).
    /// On success the cached <see cref="GmailService"/> is (re)built so a subsequent inbox load
    /// uses the new session. Never throws; failures are reported via the returned result.
    /// </summary>
    public Task<GmailSignInResult> SignInInteractiveAsync(CancellationToken cancellationToken = default) =>
        SignInInteractiveAsync(options: null, cancellationToken);

    /// <summary>
    /// Performs an <b>explicit, user-initiated</b> sign-in. By default tries silent restore first;
    /// when <paramref name="options"/>.<see cref="ConnectorLoginOptions.SkipSilentRestore"/> is
    /// <c>true</c>, opens the browser immediately. Raises <see cref="AuthStateChanged"/> on transition.
    /// </summary>
    public async Task<GmailSignInResult> SignInInteractiveAsync(
        ConnectorLoginOptions? options,
        CancellationToken cancellationToken = default)
    {
        var wasSignedIn = _gmailService != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_gmailService != null && options?.SkipSilentRestore != true)
            {
                return GmailSignInResult.Success;
            }

            if (_gmailService != null)
            {
                _gmailService.Dispose();
                _gmailService = null;
            }

            var setup = await TryPrepareAuthAsync(cancellationToken).ConfigureAwait(false);
            if (setup == null)
            {
                return GmailSignInResult.NotConfigured;
            }

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
            {
                return GmailSignInResult.Failed;
            }

            _gmailService = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = _options.ApplicationName,
            });

            return GmailSignInResult.Success;
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
        }
    }

    /// <summary>
    /// Signs out: disposes the cached client and deletes the persisted refresh token directory so
    /// the next sign-in requires fresh OAuth consent. Raises <see cref="AuthStateChanged"/> on transition.
    /// <para>
    /// Prefer <see cref="LogoutAsync"/> from async call sites. This synchronous overload blocks on the
    /// gate (via <see cref="LogoutAsync"/>) and exists only for the legacy sync
    /// <c>IConnectorAuthService.Logout</c> surface.
    /// </para>
    /// </summary>
    public void Logout() => LogoutAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Async sign-out: acquires the gate with <see cref="SemaphoreSlim.WaitAsync()"/> (never the
    /// blocking <c>Wait()</c>, which risks a UI deadlock when a concurrent sign-in/read holds the
    /// gate), disposes the cached client, and deletes the persisted refresh token directory. Raises
    /// <see cref="AuthStateChanged"/> outside the gate on transition.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var wasSignedIn = _gmailService != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gmailService?.Dispose();
            _gmailService = null;
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
            _logger.Info("[Gmail] Persisted token store deleted on logout.");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Failed to delete token store on logout: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds (and caches) the <see cref="GmailService"/> while the gate is held. Honors
    /// <paramref name="allowInteractive"/> in addition to <see cref="GmailOptions.AllowInteractiveSignIn"/>:
    /// a browser is opened only when <paramref name="allowInteractive"/> is <c>true</c> and the
    /// silent restore failed.
    /// </summary>
    private async Task<GmailService?> BuildServiceLockedAsync(bool allowInteractive, CancellationToken cancellationToken)
    {
        if (_gmailService != null)
        {
            return _gmailService;
        }

        var credential = await AcquireCredentialAsync(allowInteractive, cancellationToken).ConfigureAwait(false);
        if (credential == null)
        {
            return null;
        }

        _gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });

        return _gmailService;
    }

    private async Task<UserCredential?> AcquireCredentialAsync(bool allowInteractive, CancellationToken cancellationToken)
    {
        var setup = await TryPrepareAuthAsync(cancellationToken).ConfigureAwait(false);
        if (setup == null)
        {
            return null;
        }

        var (secrets, dataStore) = setup.Value;

        // 1) Silent restore from the token store (no browser).
        var restored = await TryRestoreAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false);
        if (restored != null)
        {
            return restored;
        }

        // 2) Interactive consent only when both the caller and the options allow it.
        if (!allowInteractive || !_options.AllowInteractiveSignIn)
        {
            _logger.Warn("[Gmail] No stored token and interactive sign-in is disabled. Mailbox unavailable.");
            return null;
        }

        return await AuthorizeInteractiveAsync(secrets, dataStore, promptAccountSelection: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the client secrets and creates the token data store, or returns <c>null</c> when
    /// secrets are missing/unreadable (mailbox stays unavailable, never throws).
    /// </summary>
    private async Task<(ClientSecrets Secrets, FileDataStore DataStore)?> TryPrepareAuthAsync(
        CancellationToken cancellationToken)
    {
        var secretsPath = await ResolveSecretsPathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secretsPath) || !File.Exists(secretsPath))
        {
            _logger.Warn($"[Gmail] client secrets not found at '{secretsPath ?? "(null)"}'. Mailbox unavailable.");
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
            _logger.Error($"[Gmail] Failed to read client secrets: {ex.Message}", ex);
            return null;
        }

        return (secrets, new FileDataStore(tokenPath, fullPath: true));
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

            _logger.Warn("[Gmail] Vault Google client secrets unavailable; config fallback is deprecated — use Secret Setup.");
        }

        var configuredPath = _options.ClientSecretsPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

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

                _logger.Info("[Gmail] Interactive sign-in completed.");
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

            _logger.Info("[Gmail] Interactive sign-in completed (account selection prompted).");
            return new UserCredential(flow, TokenUser, token);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Gmail] Interactive sign-in failed: {ex.Message}", ex);
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
            {
                return null;
            }

            var credential = new UserCredential(flow, TokenUser, token);

            if (token.IsStale)
            {
                var refreshed = await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!refreshed)
                {
                    _logger.Warn("[Gmail] Stored token is stale and could not be refreshed.");
                    return null;
                }
            }

            _logger.Info("[Gmail] Silently restored session from token store.");
            return credential;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Gmail] Silent token restore failed: {ex.Message}");
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        var wasSignedIn = _gmailService != null;
        _gmailService?.Dispose();
        _gmailService = null;
        _gate.Dispose();
        RaiseIfAuthStateChanged(wasSignedIn);
        return ValueTask.CompletedTask;
    }
}
