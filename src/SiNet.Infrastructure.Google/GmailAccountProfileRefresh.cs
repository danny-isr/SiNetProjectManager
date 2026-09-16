namespace SiNet.Infrastructure.Google;

/// <summary>
/// Resolves the displayed Google account after a profile read.
/// A temporary GetProfile failure must not look like sign-out while the session is alive.
/// </summary>
internal static class GmailAccountProfileRefresh
{
    internal static string? AfterLookup(
        bool isSignedIn,
        string? previousEmail,
        string? profileEmail,
        bool lookupFailed)
    {
        if (!isSignedIn)
            return null;

        if (lookupFailed)
            return previousEmail;

        return string.IsNullOrWhiteSpace(profileEmail)
            ? previousEmail
            : profileEmail.Trim();
    }
}
