namespace SiNet.Infrastructure.Google;

/// <summary>
/// Outcome of an explicit, user-initiated Gmail sign-in attempt
/// (<see cref="GmailClientProvider.SignInInteractiveAsync"/>). Lets the UI report a precise,
/// non-throwing status to the user.
/// </summary>
public enum GmailSignInResult
{
    /// <summary>A usable Gmail session is available (restored or freshly authorized).</summary>
    Success,

    /// <summary>
    /// No client secrets are configured (or they are unreadable), so sign-in cannot be attempted.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// Sign-in was attempted but did not complete (consent cancelled, network/OAuth error, etc.).
    /// </summary>
    Failed,
}
