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
    public event Action<bool>? AuthStateChanged
    {
        add => _provider.AuthStateChanged += value;
        remove => _provider.AuthStateChanged -= value;
    }

    /// <summary>
    /// Performs an explicit, user-initiated Google sign-in. Returns <c>true</c> when a usable
    /// session was established. Never throws for the not-configured / not-signed-in cases.
    /// </summary>
    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        var result = await _provider.SignInInteractiveAsync(cancellationToken).ConfigureAwait(false);
        return result == GmailSignInResult.Success;
    }

    /// <inheritdoc />
    public void Logout() => _provider.Logout();

    /// <summary>
    /// Attempts a silent (no-browser) restore of a previously authorized session from the token
    /// store. Returns <c>true</c> when a usable session was restored.
    /// </summary>
    public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        => _provider.TrySignInSilentlyAsync(cancellationToken);
}
