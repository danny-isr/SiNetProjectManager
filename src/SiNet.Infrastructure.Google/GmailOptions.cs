namespace SiNet.Infrastructure.Google;

/// <summary>
/// Configuration for the native Google user session (Gmail + Drive). Kept free of hard-coded paths
/// so the host can point the module at its own OAuth client, token store, and Shared Drive roots.
/// </summary>
public sealed class GmailOptions
{
    /// <summary>
    /// Absolute path to the OAuth <c>client_secret.json</c> downloaded from the Google Cloud
    /// console. When missing/empty, the gateway stays unauthenticated and reads return empty.
    /// </summary>
    public string? ClientSecretsPath { get; set; }

    /// <summary>
    /// Folder used by the OAuth <c>FileDataStore</c> to persist the refresh token. This is the
    /// new stack's own store and is intentionally separate from the legacy service's token folder.
    /// Environment variables are expanded.
    /// </summary>
    public string TokenStorePath { get; set; } = "sinet-google-token";

    /// <summary>OAuth application name reported to the Google API initializers.</summary>
    public string ApplicationName { get; set; } = "SiNet";

    /// <summary>
    /// Root Gmail label under which projects are filed. Project emails live at
    /// <c>{RootLabel}/{location}/{projectName}</c>. Defaults to the legacy root.
    /// </summary>
    public string RootLabel { get; set; } = "פרויקטים_משרד";

    /// <summary>
    /// Optional override for <see cref="EmailMailboxScope.Inbox"/> list query.
    /// Default matches Gmail Primary tab: <c>label:INBOX category:primary</c>.
    /// </summary>
    public string DefaultMailboxQuery { get; set; } = "label:INBOX category:primary";

    /// <summary>
    /// When <c>true</c>, the provider may open a browser for interactive OAuth consent if no
    /// usable token exists yet. Defaults to <c>false</c> so application startup never triggers a
    /// surprise consent prompt; a dedicated "Connect Google" action can enable it later.
    /// </summary>
    public bool AllowInteractiveSignIn { get; set; }

    /// <summary>
    /// Shared Drive id used by ProjectWork Google Drive file storage. Required together with
    /// <see cref="ProjectsRootFolderId"/> for Drive to be considered configured.
    /// </summary>
    public string? SharedDriveId { get; set; }

    /// <summary>
    /// Folder id inside the Shared Drive under which all project subtrees are created/resolved.
    /// This is the central ProjectWork Drive base folder.
    /// </summary>
    public string? ProjectsRootFolderId { get; set; }

    /// <summary>True when both Shared Drive and projects-root folder ids are set.</summary>
    public bool IsDriveConfigured =>
        !string.IsNullOrWhiteSpace(SharedDriveId) &&
        !string.IsNullOrWhiteSpace(ProjectsRootFolderId);

    /// <summary>Shared Drive id for MasterPlan Reports (defaults to <see cref="SharedDriveId"/> when empty).</summary>
    public string? ReportsSharedDriveId { get; set; }

    /// <summary>Root folder id under which R01/R02/R03 report trees are created.</summary>
    public string? ReportsRootFolderId { get; set; }

    /// <summary>R01 portfolio template spreadsheet id (required for R01 generate).</summary>
    public string? R01TemplateSpreadsheetId { get; set; }

    /// <summary>R02 hours template spreadsheet id (optional — blank creates empty sheet).</summary>
    public string? R02TemplateSpreadsheetId { get; set; }

    /// <summary>Sheets write batch size for reports.</summary>
    public int ReportsBatchSize { get; set; } = 1000;

    /// <summary>Delay between Sheets write batches (ms).</summary>
    public int ReportsBatchDelayMs { get; set; } = 100;

    /// <summary>True when Reports Shared Drive + root folder are configured.</summary>
    public bool IsReportsConfigured =>
        !string.IsNullOrWhiteSpace(ReportsSharedDriveId ?? SharedDriveId) &&
        !string.IsNullOrWhiteSpace(ReportsRootFolderId);

    /// <summary>Effective Shared Drive id for Reports.</summary>
    public string? EffectiveReportsSharedDriveId =>
        string.IsNullOrWhiteSpace(ReportsSharedDriveId) ? SharedDriveId : ReportsSharedDriveId;
}
