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
    public const string AccServiceBaseUrl = "AccService.BaseUrl";
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

    public static IReadOnlyList<string> AllManaged { get; } =
    [
        DefaultProjectTitle,
        HourPriceDefault,
        InspectionTemplatesFolderId,
        InspectionReportsFolderId,
        ReportsOutputRoot,
        InboxProjectName,
        InboxFolderName,
        AccServiceBaseUrl,
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
        .. LoggingSettingKeys.All,
    ];
}
