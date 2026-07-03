using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeSettingsSurfaceTests
{
    [Fact]
    public void NewShell_opens_native_SettingsWindow_not_legacy()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("OpenNativeSettings", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<SettingsWindow>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2.WPF_Window.SettingsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementSettingsWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_menu_is_gated_by_SystemSettingsWrite()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("OpenNativeSettings", source, StringComparison.Ordinal);
        Assert.Contains(nameof(AppFeatureCodes.SystemSettingsWrite), source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsViewModel_loads_all_settings_via_ports()
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonAppSettingsService.CreateDefaultDto());

        var systemQuery = new Mock<ISystemSettingsQueryService>();
        systemQuery.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptySystemSettings());

        var systemCommand = new Mock<ISystemSettingsCommandService>();
        var loggingCommand = new Mock<ILoggingSettingsCommandService>();
        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();
        var statusColors = new Mock<IStatusColorSettingsService>();
        statusColors.Setup(s => s.GetUserStatusColorsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        statusColors.Setup(s => s.GetGlobalStatusColorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new SettingsViewModel(
            appSettings.Object,
            systemQuery.Object,
            systemCommand.Object,
            loggingCommand.Object,
            loggingRuntime.Object,
            statusColors.Object,
            new StubCurrentUser(1));

        await vm.LoadAsync();

        appSettings.Verify(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
        systemQuery.Verify(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_calls_logging_runtime_applier_when_logging_changed()
    {
        var loaded = JsonAppSettingsService.CreateDefaultDto();
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        appSettings.Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns<UserAppSettingsDto, CancellationToken>((_, _) =>
            {
                saveCompleted.TrySetResult();
                return Task.CompletedTask;
            });

        var systemQuery = new Mock<ISystemSettingsQueryService>();
        systemQuery.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptySystemSettings());

        var systemCommand = new Mock<ISystemSettingsCommandService>();
        systemCommand.Setup(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggingCommand = new Mock<ILoggingSettingsCommandService>();
        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();
        var statusColors = new Mock<IStatusColorSettingsService>();
        statusColors.Setup(s => s.GetUserStatusColorsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        statusColors.Setup(s => s.GetGlobalStatusColorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new SettingsViewModel(
            appSettings.Object,
            systemQuery.Object,
            systemCommand.Object,
            loggingCommand.Object,
            loggingRuntime.Object,
            statusColors.Object,
            new StubCurrentUser(1));

        await vm.LoadAsync();
        vm.LoggingEnabled = true;
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        loggingRuntime.Verify(
            r => r.ApplyUserLogging(It.Is<UserLoggingSettingsDto>(d => d.LoggingEnabled)),
            Times.Once);
        appSettings.Verify(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()), Times.Once);
        systemCommand.Verify(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SystemSettingsDto CreateEmptySystemSettings()
    {
        var log = new CentralLoggingSettingsDto(null, 14, 90,
            new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Warning),
            new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
            new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
            false);

        return new SystemSettingsDto(
            new EmailOfficeSystemSettingsDto(
                SystemSettingsDefaults.DefaultProjectTitle,
                SystemSettingsDefaults.OfficeManagementProjectId,
                SystemSettingsDefaults.HourPriceDefault,
                SystemSettingsDefaults.InboxFolderNameFallback,
                null,
                10),
            new AccSystemSettingsDto(string.Empty, string.Empty, string.Empty, SystemSettingsDefaults.AccManualUploadAllowedExtensions),
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
            log);
    }

    private static string NewShellFactoryPath => Path.Combine(
        Boundary.RepoPaths.RepoRoot,
        "src",
        "SiNet.App.Wpf",
        "Shell",
        "NewShellFactory.cs");

    private sealed class StubCurrentUser(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
