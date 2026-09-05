namespace SiNet.Application.Settings;

/// <summary>
/// Resolves the Office Inbox / AccService Project Admin email.
/// Canonical source: <c>dbo.SystemSettings.AccBootstrapAdminEmail</c>.
/// Must match the AccService Admin token identity — not a hardcoded operator mailbox.
/// </summary>
public static class AccBootstrapAdminEmailResolver
{
    /// <summary>
    /// <c>SystemSettings.AccBootstrapAdminEmail</c> is the sole authority.
    /// An optional request <paramref name="requestAdminEmail"/> may only echo that value
    /// (or be empty). A differing request fails closed — never a second source of truth.
    /// </summary>
    public static string ResolveForInboxProjectAdmin(
        string? configuredFromSettings,
        string? requestAdminEmail = null)
    {
        if (string.IsNullOrWhiteSpace(configuredFromSettings))
        {
            throw new InvalidOperationException(
                "AccBootstrapAdminEmail is required to assign Office Inbox Project Admin. " +
                "Configure dbo.SystemSettings.AccBootstrapAdminEmail (and pass it into AccBootstrapService / /inbox/ensure).");
        }

        var configured = configuredFromSettings.Trim();

        if (string.IsNullOrWhiteSpace(requestAdminEmail))
        {
            return configured;
        }

        var requested = requestAdminEmail.Trim();
        if (string.Equals(requested, configured, StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        throw new InvalidOperationException(
            $"Request AdminEmail '{requested}' does not match configured AccBootstrapAdminEmail '{configured}'. " +
            "SystemSettings.AccBootstrapAdminEmail is the sole authority for Office Inbox Project Admin.");
    }
}
