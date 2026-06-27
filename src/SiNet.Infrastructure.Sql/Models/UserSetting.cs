namespace SiNetSQL.Models;

/// <summary>
/// Stores user-specific settings.
/// </summary>
public partial class UserSetting
{
    public int Id { get; set; }

    public int SiuserId { get; set; }

    /// <summary>
    /// If true, the Tasks panel opens automatically after filing an email.
    /// Default is true.
    /// </summary>
    public bool AutoOpenTasksPanelAfterFiling { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════════════
    // GMAIL THROTTLE SETTINGS
    // Per-user rate limiting configuration for Gmail API calls.
    // When null/0, defaults from GmailQuotaConstants are used.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimum delay between sequential Gmail API requests (milliseconds).
    /// Default: 150ms (production-safe). Set to 0 or null to use system default.
    /// Lower values = faster but higher risk of 429.
    /// </summary>
    public int? GmailMinDelayMs { get; set; }

    /// <summary>
    /// Maximum parallel Gmail API requests allowed for this user.
    /// Default: 1 (sequential). Higher values increase throughput but risk 429.
    /// </summary>
    public int? GmailMaxParallelRequests { get; set; }

    // Navigation property
    public virtual Siuser Siuser { get; set; } = null!;
}
