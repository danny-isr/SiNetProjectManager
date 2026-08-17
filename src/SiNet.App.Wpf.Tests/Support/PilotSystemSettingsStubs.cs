using SiNet.Application.Settings;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.App.Wpf.Tests.Support;

/// <summary>
/// In-memory <see cref="ISystemSettingsQueryService"/> that enables Pilot with a broad allowlist
/// so existing workflow StartAsync harnesses keep working under fail-closed Pilot defaults.
/// </summary>
public sealed class PermissivePilotSystemSettingsQueryService : ISystemSettingsQueryService
{
    private readonly SystemSettingsDto _settings;

    public PermissivePilotSystemSettingsQueryService(
        int allowedUserId = ProposalWorkflowHarness.UserId,
        string allowedCodesCsv =
            $"{WorkflowCodes.Proposal},{WorkflowCodes.Opinion},{WorkflowCodes.PlanningWorkflow}," +
            $"{WorkflowCodes.Review},{WorkflowCodes.MaterialIntake},{WorkflowCodes.Outsourcing}")
    {
        _settings = new SystemSettingsDto(
            new EmailOfficeSystemSettingsDto(
                SystemSettingsDefaults.DefaultProjectTitle,
                SystemSettingsDefaults.OfficeManagementProjectId,
                SystemSettingsDefaults.HourPriceDefault,
                SystemSettingsDefaults.InboxFolderNameFallback,
                InboxProjectName: null,
                AccViewerMaxTabs: 10),
            new AccSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty,
                SystemSettingsDefaults.AccManualUploadAllowedExtensions),
            new InspectionSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty),
            new InspectionStatusLabelsDto(
                SystemSettingsDefaults.StatusLabelPassed,
                SystemSettingsDefaults.StatusLabelFailed,
                SystemSettingsDefaults.StatusLabelRecurringFailed,
                SystemSettingsDefaults.StatusLabelNotApplicable),
            new AiSystemSettingsDto(
                SystemSettingsDefaults.OllamaBaseUrl,
                SystemSettingsDefaults.OllamaModel,
                new AiModelLevelSelectionDto(string.Empty, string.Empty),
                new AiModelLevelSelectionDto(string.Empty, string.Empty),
                new AiModelLevelSelectionDto(string.Empty, string.Empty),
                new AiModelLevelSelectionDto(string.Empty, string.Empty),
                string.Empty),
            new CentralLoggingSettingsDto(
                null, 14, 90,
                new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Warning),
                new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                false),
            new WorkflowSystemSettingsDto(
                SystemSettingsDefaults.WorkflowMaxOpenChildInstances,
                PilotEnabled: true,
                PilotAllowedUserIds: allowedUserId.ToString(),
                PilotAllowedWorkflowCodes: allowedCodesCsv),
            new ProjectWorkSystemSettingsDto(SystemSettingsDefaults.ProjectWorkScanExclusionRules));
    }

    public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings);
}

/// <summary>Configurable Pilot settings stub for focused gate tests.</summary>
public sealed class StubPilotSystemSettingsQueryService(WorkflowSystemSettingsDto workflow) : ISystemSettingsQueryService
{
    private readonly SystemSettingsDto _settings = new(
        new EmailOfficeSystemSettingsDto(
            SystemSettingsDefaults.DefaultProjectTitle,
            SystemSettingsDefaults.OfficeManagementProjectId,
            SystemSettingsDefaults.HourPriceDefault,
            SystemSettingsDefaults.InboxFolderNameFallback,
            null,
            10),
        new AccSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty,
            SystemSettingsDefaults.AccManualUploadAllowedExtensions),
        new InspectionSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty),
        new InspectionStatusLabelsDto(
            SystemSettingsDefaults.StatusLabelPassed,
            SystemSettingsDefaults.StatusLabelFailed,
            SystemSettingsDefaults.StatusLabelRecurringFailed,
            SystemSettingsDefaults.StatusLabelNotApplicable),
        new AiSystemSettingsDto(
            SystemSettingsDefaults.OllamaBaseUrl,
            SystemSettingsDefaults.OllamaModel,
            new AiModelLevelSelectionDto(string.Empty, string.Empty),
            new AiModelLevelSelectionDto(string.Empty, string.Empty),
            new AiModelLevelSelectionDto(string.Empty, string.Empty),
            new AiModelLevelSelectionDto(string.Empty, string.Empty),
            string.Empty),
        new CentralLoggingSettingsDto(
            null, 14, 90,
            new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Warning),
            new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
            new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
            false),
        workflow,
        new ProjectWorkSystemSettingsDto(SystemSettingsDefaults.ProjectWorkScanExclusionRules));

    public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings);
}
