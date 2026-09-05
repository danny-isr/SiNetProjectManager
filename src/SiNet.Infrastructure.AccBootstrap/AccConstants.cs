namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Centralized constants for ACC (Autodesk Construction Cloud) integration.
/// These values are used across multiple services for consistency.
/// </summary>
public static class AccConstants
{
    /// <summary>
    /// The key used in AccSystemResource table for the Office Inbox configuration.
    /// Used by: AccBootstrapService, EmailIngestionService, DevHarness tests.
    /// </summary>
    public const string OfficeInboxResourceKey = "OfficeInbox";

    /// <summary>
    /// Default members to add to the Office Inbox project.
    /// These users get "viewer" access to docs. The Office Inbox is a system
    /// project: regular users may view files, while writes/metadata changes are
    /// reserved for the explicit system/admin account.
    /// 
    /// NOTE: AccBootstrapAdminEmail is assigned separately via AssignProjectAdminAsync
    /// with "administrator" access. Do NOT include that service account here.
    /// 
    /// To modify: Add or remove email addresses from this list.
    /// Future: Move to appsettings.json for runtime configuration.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultInboxMembers = new[]
    {
        "Lilach@si-eng.co.il"
        // Add more members here as needed
    };
}
