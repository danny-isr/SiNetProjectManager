using SiNet.Application.Common;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Native implementation of the shared <see cref="IConnectorAuthService"/> port for Google,
/// adapting the <see cref="GmailClientProvider"/> session model onto the connector-agnostic
/// auth surface. This is the native counterpart of the legacy <c>GoogleService</c> auth/health
/// bridge: it exposes the current signed-in state and forwards the provider's
/// <see cref="GmailClientProvider.AuthStateChanged"/> notifications so health/status consumers
/// can react without depending on the concrete provider or the Gmail API.
/// <para>
/// Mapping: <see cref="IsAuthenticated"/> ⇒ <see cref="GmailClientProvider.IsSignedIn"/>;
/// <see cref="LoginAsync"/> ⇒ <see cref="GmailClientProvider.SignInInteractiveAsync"/>;
/// <see cref="TryRestoreSessionAsync"/> ⇒ <see cref="GmailClientProvider.TrySignInSilentlyAsync"/>;
/// <see cref="Logout"/> ⇒ <see cref="GmailClientProvider.Logout"/>.
/// </para>
/// </summary>
public sealed class GmailConnectorAuthService : IConnectorAuthService
{
    private readonly GmailClientProvider _provider;

    public GmailConnectorAuthService(GmailClientProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public bool IsAuthenticated => _provider.IsSignedIn;

    /// <inheritdoc />
    public string? ConnectedAccountEmail { get; private set; }

    /// <inheritdoc />
    public event Action<bool>? AuthStateChanged
    {
        add => _provider.AuthStateChanged += value;
        remove => _provider.AuthStateChanged -= value;
    }

    /// <summary>
    /// Performs an explicit, user-initiated Google sign-in. Returns <c>true</c> when a usable
    /// session was established. Never throws for the not-configured / not-signed-in cases.
    /// </summary>
    public async Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await _provider.SignInInteractiveAsync(options, cancellationToken).ConfigureAwait(false);
        if (result == GmailSignInResult.Success)
        {
            await RefreshAccountProfileAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Logout()
    {
        ConnectedAccountEmail = null;
        _provider.Logout();
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ConnectedAccountEmail = null;
        await _provider.LogoutAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts a silent (no-browser) restore of a previously authorized session from the token
    /// store. Returns <c>true</c> when a usable session was restored.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var restored = await _provider.TrySignInSilentlyAsync(cancellationToken).ConfigureAwait(false);
        if (restored)
        {
            await RefreshAccountProfileAsync(cancellationToken).ConfigureAwait(false);
        }

        return restored;
    }

    /// <inheritdoc />
    public async Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!_provider.IsSignedIn)
        {
            ConnectedAccountEmail = null;
            return;
        }

        try
        {
            var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
            if (gmail is null)
            {
                ConnectedAccountEmail = null;
                return;
            }

            var profile = await gmail.Users.GetProfile("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            ConnectedAccountEmail = string.IsNullOrWhiteSpace(profile.EmailAddress)
                ? null
                : profile.EmailAddress.Trim();
        }
        catch
        {
            ConnectedAccountEmail = null;
        }
    }
}
