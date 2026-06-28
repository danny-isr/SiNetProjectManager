using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using SiNet.Application.Abstractions.Logging;

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
    };
    private const string TokenUser = "user";

    private readonly GmailOptions _options;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GmailService? _gmailService;

    public GmailClientProvider(GmailOptions options, IAppLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The root Gmail label under which projects are filed.</summary>
    public string RootLabel => _options.RootLabel;

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
    public async Task<GmailSignInResult> SignInInteractiveAsync(CancellationToken cancellationToken = default)
    {
        var wasSignedIn = _gmailService != null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_gmailService != null)
            {
                return GmailSignInResult.Success;
            }

            var setup = await TryPrepareAuthAsync(cancellationToken).ConfigureAwait(false);
            if (setup == null)
            {
                return GmailSignInResult.NotConfigured;
            }

            var (secrets, dataStore) = setup.Value;

            var credential =
                await TryRestoreAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false)
                ?? await AuthorizeInteractiveAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false);

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
    /// Drops the cached Gmail session so the provider reports as signed-out. Does not revoke or
    /// delete the persisted refresh token, so a subsequent <see cref="TrySignInSilentlyAsync"/>
    /// can restore the session without a browser. Raises <see cref="AuthStateChanged"/> when the
    /// state actually transitions from signed-in to signed-out.
    /// </summary>
    public void Logout()
    {
        var wasSignedIn = _gmailService != null;
        _gate.Wait();
        try
        {
            _gmailService?.Dispose();
            _gmailService = null;
        }
        finally
        {
            _gate.Release();
            RaiseIfAuthStateChanged(wasSignedIn);
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

        return await AuthorizeInteractiveAsync(secrets, dataStore, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the client secrets and creates the token data store, or returns <c>null</c> when
    /// secrets are missing/unreadable (mailbox stays unavailable, never throws).
    /// </summary>
    private async Task<(ClientSecrets Secrets, FileDataStore DataStore)?> TryPrepareAuthAsync(
        CancellationToken cancellationToken)
    {
        var secretsPath = _options.ClientSecretsPath;
        if (string.IsNullOrWhiteSpace(secretsPath) || !File.Exists(secretsPath))
        {
            _logger.Warn($"[Gmail] client secrets not found at '{secretsPath}'. Mailbox unavailable.");
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

    private async Task<UserCredential?> AuthorizeInteractiveAsync(
        ClientSecrets secrets,
        IDataStore dataStore,
        CancellationToken cancellationToken)
    {
        try
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
