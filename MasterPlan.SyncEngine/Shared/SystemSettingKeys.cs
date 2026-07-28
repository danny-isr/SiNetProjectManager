namespace MasterPlan.SyncEngine.Shared;

/// <summary>
/// Well-known setting keys used across the application.
/// Centralizes magic strings into a single source of truth.
/// <para>
/// Lives in its own file (no EF Core / model dependencies) so it can be shared
/// via <c>&lt;Compile Include /&gt;</c> by lightweight projects (e.g. MasterPlan.SyncEngine)
/// that need the keys for the centralized logging loader but cannot reference
/// the full SiNetSQL assembly.
/// </para>
/// </summary>
public static class SystemSettingKeys
{
    public const string DefaultProjectTitle = "DefaultProjectTitle";
    public const string HourPriceDefault = "HourPriceDefault";
    public const string InspectionTemplatesFolderId = "InspectionTemplatesFolderId";
    public const string InspectionReportsFolderId = "InspectionReportsFolderId";
    public const string ReportsOutputRoot = "ReportsOutputRoot";

    // ACC Email Inbox settings
    public const string InboxProjectName = "InboxProjectName";
    public const string InboxFolderName = "InboxFolderName";

    /// <summary>
    /// URL of the internal SiOffice.AccService used by the WPF client for
    /// privileged ACC operations. Empty = local in-process ACC provisioning.
    /// </summary>
    public const string AccServiceBaseUrl = "AccService.BaseUrl";

    /// <summary>
    /// Semicolon-separated TLS certificate thumbprints that clients may trust for AccService
    /// when the server presents a self-signed certificate. Not a secret.
    /// </summary>
    public const string AccServicePinnedCertificateThumbprints = "AccService.PinnedCertificateThumbprints";

    /// <summary>
    /// Exact name of the ACC project TEMPLATE used as the source for newly-created
    /// per-Place ACC projects. When non-empty, <c>EnsureProjectMappingAsync</c>
    /// resolves the template by name and creates the new project FROM it. Templates
    /// carry industry-role folder ACLs (e.g. Engineer = Edit, Administrator = Manage)
    /// that propagate to derived projects, removing the need for explicit
    /// <c>SetFolderPermissions</c> API calls. Leave empty to disable template-based
    /// creation (legacy behavior).
    /// </summary>
    public const string AccProjectTemplateName = "AccProjectTemplateName";

    /// <summary>
    /// Email of the dedicated ACC bootstrap admin. This is a SERVICE account that
    /// is intentionally NOT modeled as an SIUser:
    /// <list type="bullet">
    ///   <item>It does not appear in the user-management UI.</item>
    ///   <item>It is assigned as Project Admin on every newly-created ACC project
    ///         (replaces the legacy <c>SIUser.AccUserType=Admin</c> lookup).</item>
    ///   <item><c>ProvisionUsersToProjectAsync</c> SKIPS this email - it is never
    ///         downgraded to <c>docs=member</c>, never role-reassigned, never
    ///         folder-permission-tweaked. Whatever ACC permissions it has stay
    ///         exactly as-is.</item>
    /// </list>
    /// Leave empty to fall back to the legacy SIUser-based resolution.
    /// </summary>
    public const string AccBootstrapAdminEmail = "AccBootstrapAdminEmail";

    // Status label mappings (DB key → Hebrew display label)
    public const string StatusLabelPassed = "StatusLabel_Passed";
    public const string StatusLabelFailed = "StatusLabel_Failed";
    public const string StatusLabelRecurringFailed = "StatusLabel_RecurringFailed";
    public const string StatusLabelNotApplicable = "StatusLabel_NotApplicable";

    // AI (Ollama) settings
    public const string OllamaBaseUrl = "OllamaBaseUrl";
    public const string OllamaModel = "OllamaModel";

    // AI Model Catalog — selected models per usage level.
    // Each level stores both the model name and the provider it belongs to so the
    // calling code can route the request to the right API (Ollama / Gemini / OpenAI-compatible).
    /// <summary>Model name selected for the "Simple" level (fast, cheap, low-quality OK).</summary>
    public const string AiModelSimple = "AiModel.Simple";
    /// <summary>Model name selected for the "QualityCheck" level (grammar / proofreading).</summary>
    public const string AiModelQualityCheck = "AiModel.QualityCheck";
    /// <summary>Model name selected for the "Writing" level (rephrasing / drafting).</summary>
    public const string AiModelWriting = "AiModel.Writing";
    /// <summary>Model name selected for the "DeepAnalysis" level (long-context reasoning, extraction).</summary>
    public const string AiModelDeepAnalysis = "AiModel.DeepAnalysis";

    /// <summary>Provider name (Ollama / Gemini / OpenAICompatible) for the Simple level.</summary>
    public const string AiProviderSimple = "AiProvider.Simple";
    /// <summary>Provider name for the QualityCheck level.</summary>
    public const string AiProviderQualityCheck = "AiProvider.QualityCheck";
    /// <summary>Provider name for the Writing level.</summary>
    public const string AiProviderWriting = "AiProvider.Writing";
    /// <summary>Provider name for the DeepAnalysis level.</summary>
    public const string AiProviderDeepAnalysis = "AiProvider.DeepAnalysis";

    /// <summary>
    /// CSV of cloud models the user has "configured" (made selectable in the AI dropdowns)
    /// from the AI Model Catalog. Each entry has the form "Provider|ModelName"
    /// (e.g. "Gemini|gemini-2.5-flash,Gemini|gemini-2.5-pro"). Local Ollama models are
    /// discovered live via GET /api/tags and are NOT stored here.
    /// </summary>
    public const string AiConfiguredCloudModels = "AiConfiguredCloudModels";

    // Drawing stamp settings
    public const string StampTemplatePath = "StampTemplatePath";

    // Workflow settings
    /// <summary>
    /// Project ID used as placeholder for project-independent workflows (e.g. Proposal).
    /// The user can change this in Management Settings. Default: 136 (ניהול משרד).
    /// </summary>
    public const string OfficeManagementProjectId = "OfficeManagementProjectId";

    /// <summary>
    /// Maximum number of ACC viewer tabs that can be open simultaneously in the
    /// "בעבודה 2" window. Globally configured by the administrator. Default: 10.
    /// </summary>
    public const string AccViewerMaxTabs = "AccViewerMaxTabs";

    /// <summary>
    /// Comma-separated list of file extensions (with leading dot, case-insensitive)
    /// allowed for the manual "העלה ל-ACC" / orphan-restore flows in the unified
    /// tree ("בעבודה 2"). ACC's viewer/translation reliably handles only a small
    /// set of formats; uploads outside this list are blocked with a user message.
    /// Default: ".pdf,.dwf,.dwg".
    /// </summary>
    public const string AccManualUploadAllowedExtensions = "AccManualUploadAllowedExtensions";

    // ═══════════════════════════════════════════════════════════════════
    //  Centralized Logging settings
    //  Read at startup by every app (Client / AccService / SyncEngine) via
    //  SiNet.Infrastructure.Logging.CentralLoggingSettings.LoadFromDatabase.
    //  Single source of truth — configured in the Admin UI, applies to all
    //  installations once they reconnect to the DB.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>UNC or local path for the centralized log share. Empty = central logging disabled.</summary>
    public const string LoggingCentralLogPath = "Logging.CentralLogPath";

    /// <summary>Local rolling-file retention in days (applies to every app).</summary>
    public const string LoggingLocalRetentionDays = "Logging.LocalRetentionDays";

    /// <summary>Central rolling-file retention in days (applies to every app).</summary>
    public const string LoggingCentralRetentionDays = "Logging.CentralRetentionDays";

    /// <summary>Minimum level for the WPF client's local log file.</summary>
    public const string LoggingClientFileLevel = "Logging.Client.FileLevel";
    /// <summary>Minimum level for the WPF client's central (network) log.</summary>
    public const string LoggingClientCentralLevel = "Logging.Client.CentralLevel";

    /// <summary>Minimum level for SiOffice.AccService's local log file.</summary>
    public const string LoggingAccServiceFileLevel = "Logging.AccService.FileLevel";
    /// <summary>Minimum level for SiOffice.AccService's central (network) log.</summary>
    public const string LoggingAccServiceCentralLevel = "Logging.AccService.CentralLevel";

    /// <summary>Minimum level for MasterPlan.SyncEngine's local log file.</summary>
    public const string LoggingSyncEngineFileLevel = "Logging.SyncEngine.FileLevel";
    /// <summary>Minimum level for MasterPlan.SyncEngine's central (network) log.</summary>
    public const string LoggingSyncEngineCentralLevel = "Logging.SyncEngine.CentralLevel";
}
