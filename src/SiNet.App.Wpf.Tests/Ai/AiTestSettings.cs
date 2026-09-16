using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Tests.Ai;

internal static class AiTestSettings
{
    public static SystemSettingsDto Create(AiSystemSettingsDto ai) =>
        new(
            new EmailOfficeSystemSettingsDto(
                SystemSettingsDefaults.DefaultProjectTitle,
                SystemSettingsDefaults.OfficeManagementProjectId,
                SystemSettingsDefaults.HourPriceDefault,
                SystemSettingsDefaults.InboxFolderNameFallback,
                null,
                10),
            new AccSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty, SystemSettingsDefaults.AccManualUploadAllowedExtensions),
            new InspectionSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty),
            new InspectionStatusLabelsDto(
                SystemSettingsDefaults.StatusLabelPassed,
                SystemSettingsDefaults.StatusLabelFailed,
                SystemSettingsDefaults.StatusLabelRecurringFailed,
                SystemSettingsDefaults.StatusLabelNotApplicable),
            ai,
            new CentralLoggingSettingsDto(
                null,
                14,
                90,
                new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                false),
            new WorkflowSystemSettingsDto(SystemSettingsDefaults.WorkflowMaxOpenChildInstances),
            new ProjectWorkSystemSettingsDto(SystemSettingsDefaults.ProjectWorkScanExclusionRules),
            SystemSettingsDefaults.Diagnostics);
}
