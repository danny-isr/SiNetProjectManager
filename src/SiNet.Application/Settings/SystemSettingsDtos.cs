namespace SiNet.Application.Settings;

/// <summary>Email routing and office workflow globals.</summary>
public sealed record EmailOfficeSystemSettingsDto(
    string DefaultProjectTitle,
    string OfficeManagementProjectId,
    string HourPriceDefault,
    string InboxFolderName,
    string? InboxProjectName,
    int AccViewerMaxTabs);

/// <summary>ACC integration globals.</summary>
public sealed record AccSystemSettingsDto(
    string AccServiceBaseUrl,
    string AccBootstrapAdminEmail,
    string AccProjectTemplateName,
    string AccManualUploadAllowedExtensions);

/// <summary>Google Drive folder IDs for inspection.</summary>
public sealed record InspectionSystemSettingsDto(
    string InspectionTemplatesFolderId,
    string InspectionReportsFolderId,
    string ReportsOutputRoot,
    string StampTemplatePath);

/// <summary>Hebrew labels for inspection status codes.</summary>
public sealed record InspectionStatusLabelsDto(
    string Passed,
    string Failed,
    string RecurringFailed,
    string NotApplicable);

/// <summary>One AI usage level (model + provider).</summary>
public sealed record AiModelLevelSelectionDto(string Model, string Provider);

/// <summary>Ollama + per-level AI model routing.</summary>
public sealed record AiSystemSettingsDto(
    string OllamaBaseUrl,
    string OllamaModel,
    AiModelLevelSelectionDto Simple,
    AiModelLevelSelectionDto QualityCheck,
    AiModelLevelSelectionDto Writing,
    AiModelLevelSelectionDto DeepAnalysis,
    string ConfiguredCloudModelsCsv);

/// <summary>
/// All global/admin settings from <c>dbo.SystemSettings</c>. Includes centralized logging.
/// </summary>
public sealed record SystemSettingsDto(
    EmailOfficeSystemSettingsDto EmailOffice,
    AccSystemSettingsDto Acc,
    InspectionSystemSettingsDto Inspection,
    InspectionStatusLabelsDto StatusLabels,
    AiSystemSettingsDto Ai,
    CentralLoggingSettingsDto Logging);

/// <summary>Legacy defaults when DB rows are missing (from ManagementSettingsWindow / catalog).</summary>
public static class SystemSettingsDefaults
{
    public const string DefaultProjectTitle = "ניהול  משרד - כללי";
    public const string HourPriceDefault = "280";
    public const string OfficeManagementProjectId = "136";
    public const string AccViewerMaxTabs = "10";
    public const string InboxFolderNameFallback = "_Inbox";
    public const string AccManualUploadAllowedExtensions = ".pdf,.dwf,.dwg";
    public const string OllamaBaseUrl = "http://localhost:11434";
    public const string OllamaModel = "gemma3:4b";
    public const string StatusLabelPassed = "מקובל";
    public const string StatusLabelFailed = "הערה";
    public const string StatusLabelRecurringFailed = "הערה חוזרת";
    public const string StatusLabelNotApplicable = "לא רלוונטי";
}
