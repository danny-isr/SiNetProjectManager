namespace SiNet.Application.Settings;

/// <summary>
/// Resolves the Office Inbox / AccService Project Admin email.
/// Canonical source: <c>dbo.SystemSettings.AccBootstrapAdminEmail</c>.
/// Must match the AccService Admin token identity — not a hardcoded operator mailbox.
/// </summary>
public static class AccBootstrapAdminEmailResolver
{
    /// <summary>
    /// Prefer an explicit request override, then the configured system setting.
    /// Fail closed when neither is present — never invent a historical personal mailbox.
    /// </summary>
    public static string ResolveForInboxProjectAdmin(
        string? configuredFromSettings,
        string? requestOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(requestOverride))
        {
            return requestOverride.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuredFromSettings))
        {
            return configuredFromSettings.Trim();
        }

        throw new InvalidOperationException(
            "AccBootstrapAdminEmail is required to assign Office Inbox Project Admin. " +
            "Configure dbo.SystemSettings.AccBootstrapAdminEmail (and pass it into AccBootstrapService / /inbox/ensure).");
    }
}
