namespace SiNet.Application.Settings;

/// <summary>
/// Well-known <c>SystemSettings.SettingKey</c> values. Mirrors legacy
/// <c>SiNetSQL.Services.SystemSettingKeys</c> without referencing SiNetSQL.
/// </summary>
public static class SystemSettingKeys
{
    public const string DefaultProjectTitle = "DefaultProjectTitle";
    public const string HourPriceDefault = "HourPriceDefault";
    public const string InspectionTemplatesFolderId = "InspectionTemplatesFolderId";
    public const string InspectionReportsFolderId = "InspectionReportsFolderId";
    public const string ReportsOutputRoot = "ReportsOutputRoot";
    public const string InboxProjectName = "InboxProjectName";
    public const string InboxFolderName = "InboxFolderName";

    /// <summary>
    /// When true, email enter / label refresh may rename Gmail project leaf labels to current
    /// <c>NameAndNumber</c> (identity = leading <c>(Number)</c>). Stored as "true"/"false".
    /// </summary>
    public const string EmailAutoSyncProjectLabelNames = "Email.AutoSyncProjectLabelNames";
    public const string AccServiceBaseUrl = "AccService.BaseUrl";

    /// <summary>
    /// Semicolon-separated TLS certificate thumbprints that clients may trust for AccService
    /// when the server presents a self-signed certificate. Not a secret.
    /// </summary>
    public const string AccServicePinnedCertificateThumbprints = "AccService.PinnedCertificateThumbprints";

    /// <summary>
    /// Expected Autodesk profile email for the AccService 3-legged Account Admin token
    /// (steady-state: <c>siad@si-eng.co.il</c>). Not inferred from SIUser.
    /// </summary>
    public const string AccServiceExpectedAdminEmail = "AccService.ExpectedAdminEmail";

    public const string AccProjectTemplateName = "AccProjectTemplateName";
    public const string AccBootstrapAdminEmail = "AccBootstrapAdminEmail";
    public const string StatusLabelPassed = "StatusLabel_Passed";
    public const string StatusLabelFailed = "StatusLabel_Failed";
    public const string StatusLabelRecurringFailed = "StatusLabel_RecurringFailed";
    public const string StatusLabelNotApplicable = "StatusLabel_NotApplicable";
    public const string OllamaBaseUrl = "OllamaBaseUrl";
    public const string OllamaModel = "OllamaModel";
    public const string AiModelSimple = "AiModel.Simple";
    public const string AiModelQualityCheck = "AiModel.QualityCheck";
    public const string AiModelWriting = "AiModel.Writing";
    public const string AiModelDeepAnalysis = "AiModel.DeepAnalysis";
    public const string AiProviderSimple = "AiProvider.Simple";
    public const string AiProviderQualityCheck = "AiProvider.QualityCheck";
    public const string AiProviderWriting = "AiProvider.Writing";
    public const string AiProviderDeepAnalysis = "AiProvider.DeepAnalysis";
    public const string AiConfiguredCloudModels = "AiConfiguredCloudModels";
    public const string StampTemplatePath = "StampTemplatePath";
    public const string OfficeManagementProjectId = "OfficeManagementProjectId";
    public const string AccViewerMaxTabs = "AccViewerMaxTabs";
    public const string AccManualUploadAllowedExtensions = "AccManualUploadAllowedExtensions";
    public const string WorkflowMaxOpenChildInstances = "Workflow.MaxOpenChildInstances";

    /// <summary>
    /// Controlled Production Pilot kill-switch. Absent / empty / malformed → <c>false</c> (fail-closed).
    /// When false, new root workflow starts via <c>IWorkflowCommandService.StartAsync</c> are blocked.
    /// Does not stop existing instances, task completion, or child starts under a parent.
    /// </summary>
    public const string PilotEnabled = "Pilot.Enabled";

    /// <summary>CSV of <c>SIUser.Id</c> values allowed to start root workflows when Pilot is enabled.</summary>
    public const string PilotAllowedUserIds = "Pilot.AllowedUserIds";

    /// <summary>
    /// CSV of <c>WorkflowDefinition.Code</c> values allowed for root starts when Pilot is enabled
    /// (e.g. <c>Proposal,Opinion</c>). Empty → no codes allowed.
    /// </summary>
    public const string PilotAllowedWorkflowCodes = "Pilot.AllowedWorkflowCodes";

    /// <summary>
    /// CSV of ProjectWork scan exclusion rules: tokens starting with <c>.</c> are extensions;
    /// other tokens (e.g. <c>~$</c>) are file-name prefixes. Sidecar companions stay hard-coded.
    /// </summary>
    public const string ProjectWorkScanExclusionRules = "ProjectWork.ScanExclusionRules";

    /// <summary>
    /// Root folder for workstation crash reports (DEV-010). Empty means «derive from
    /// <see cref="LoggingSettingKeys.CentralLogPath"/>». Reports land under <c>{root}\{MachineName}\</c>.
    /// </summary>
    public const string DiagnosticsCrashReportSharePath = "Diagnostics.CrashReportSharePath";

    /// <summary>CSV of process-name fragments the crash report treats as «our» applications.</summary>
    public const string DiagnosticsCrashAppFilters = "Diagnostics.CrashAppFilters";

    /// <summary>Default number of days a crash report looks back.</summary>
    public const string DiagnosticsCrashLookbackDays = "Diagnostics.CrashLookbackDays";

    /// <summary>Age after which a saved crash report may be deleted from the share.</summary>
    public const string DiagnosticsCrashReportRetentionDays = "Diagnostics.CrashReportRetentionDays";

    public static IReadOnlyList<string> AllManaged { get; } =
    [
        DefaultProjectTitle,
        HourPriceDefault,
        InspectionTemplatesFolderId,
        InspectionReportsFolderId,
        ReportsOutputRoot,
        InboxProjectName,
        InboxFolderName,
        EmailAutoSyncProjectLabelNames,
        AccServiceBaseUrl,
        AccServicePinnedCertificateThumbprints,
        AccServiceExpectedAdminEmail,
        AccProjectTemplateName,
        AccBootstrapAdminEmail,
        StatusLabelPassed,
        StatusLabelFailed,
        StatusLabelRecurringFailed,
        StatusLabelNotApplicable,
        OllamaBaseUrl,
        OllamaModel,
        AiModelSimple,
        AiModelQualityCheck,
        AiModelWriting,
        AiModelDeepAnalysis,
        AiProviderSimple,
        AiProviderQualityCheck,
        AiProviderWriting,
        AiProviderDeepAnalysis,
        AiConfiguredCloudModels,
        StampTemplatePath,
        OfficeManagementProjectId,
        AccViewerMaxTabs,
        AccManualUploadAllowedExtensions,
        WorkflowMaxOpenChildInstances,
        PilotEnabled,
        PilotAllowedUserIds,
        PilotAllowedWorkflowCodes,
        ProjectWorkScanExclusionRules,
        DiagnosticsCrashReportSharePath,
        DiagnosticsCrashAppFilters,
        DiagnosticsCrashLookbackDays,
        DiagnosticsCrashReportRetentionDays,
        .. LoggingSettingKeys.All,
    ];
}
