using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeSettingsSurfaceTests
{
    [Fact]
    public void NewShell_opens_native_settings_via_factory_not_legacy()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("ISettingsWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("CreatePersonal", source, StringComparison.Ordinal);
        Assert.Contains("CreateSystemAdmin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2.WPF_Window.SettingsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementSettingsWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Personal_settings_menu_requires_authenticated_user_not_admin_feature()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("הגדרות אישיות", source, StringComparison.Ordinal);
        Assert.Contains("HasAuthenticatedUser", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativePersonalSettings", source, StringComparison.Ordinal);
    }

    [Fact]
    public void System_settings_menu_requires_SystemSettingsWrite()
    {
        var source = File.ReadAllText(NewShellFactoryPath);
        Assert.Contains("הגדרות מערכת", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeSystemSettings", source, StringComparison.Ordinal);
        Assert.Contains(nameof(AppFeatureCodes.SystemSettingsWrite), source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personal_scope_shows_only_personal_flags_for_regular_user()
    {
        var vm = CreateViewModel(isAdmin: false, userId: 42, SettingsSurfaceScope.Personal);
        await vm.LoadAsync();

        Assert.True(vm.CanViewPersonalSettings);
        Assert.True(vm.CanEditPersonalSettings);
        Assert.False(vm.CanViewSystemSettings);
        Assert.False(vm.CanEditSystemSettings);
        Assert.False(vm.CanViewGlobalStatusColors);
    }

    [Fact]
    public async Task System_scope_shows_only_admin_flags_for_administrator()
    {
        var vm = CreateViewModel(isAdmin: true, userId: 1, SettingsSurfaceScope.SystemAdmin);
        await vm.LoadAsync();

        Assert.False(vm.CanViewPersonalSettings);
        Assert.False(vm.CanEditPersonalSettings);
        Assert.True(vm.CanViewSystemSettings);
        Assert.True(vm.CanEditSystemSettings);
        Assert.True(vm.CanViewGlobalStatusColors);
        Assert.True(vm.CanEditGlobalStatusColors);
    }

    [Fact]
    public async Task Regular_user_system_scope_has_no_visible_tabs()
    {
        var vm = CreateViewModel(isAdmin: false, userId: 1, SettingsSurfaceScope.SystemAdmin);
        await vm.LoadAsync();

        Assert.False(vm.CanViewSystemSettings);
        Assert.False(vm.CanEditSystemSettings);
    }

    [Fact]
    public async Task Personal_save_writes_user_settings_only_not_global()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var systemCommand = new Mock<ISystemSettingsCommandService>();
        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();

        var vm = CreateViewModel(
            isAdmin: false,
            userId: 1,
            SettingsSurfaceScope.Personal,
            appSettings,
            systemCommand: systemCommand,
            loggingRuntime: loggingRuntime);

        await vm.LoadAsync();
        vm.LoggingEnabled = true;
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        appSettings.Verify(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()), Times.Once);
        systemCommand.Verify(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()), Times.Never);
        loggingRuntime.Verify(r => r.ApplyUserLogging(It.Is<UserLoggingSettingsDto>(d => d.LoggingEnabled)), Times.Once);
    }

    [Fact]
    public async Task Admin_system_save_writes_global_settings_with_restart_message()
    {
        var systemSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = new Mock<IAppSettingsService>();
        var systemCommand = new Mock<ISystemSettingsCommandService>();
        systemCommand.Setup(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns<SystemSettingsDto, CancellationToken>((_, _) =>
            {
                systemSaved.TrySetResult();
                return Task.CompletedTask;
            });

        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            appSettings,
            systemCommand: systemCommand);

        await vm.LoadAsync();
        vm.SaveCommand.Execute(null);
        await systemSaved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        systemCommand.Verify(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()), Times.Once);
        appSettings.Verify(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("הפעלה מחדש", vm.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personal_load_does_not_query_global_system_settings()
    {
        var systemQuery = new Mock<ISystemSettingsQueryService>();
        var vm = CreateViewModel(
            isAdmin: false,
            userId: 5,
            SettingsSurfaceScope.Personal,
            systemQuery: systemQuery);

        await vm.LoadAsync();

        systemQuery.Verify(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SettingsViewModel CreateViewModel(
        bool isAdmin,
        int userId,
        SettingsSurfaceScope scope,
        Mock<IAppSettingsService>? appSettings = null,
        Mock<ISystemSettingsQueryService>? systemQuery = null,
        Mock<ISystemSettingsCommandService>? systemCommand = null,
        Mock<ILoggingRuntimeApplier>? loggingRuntime = null)
    {
        appSettings ??= MockAppSettings();
        systemQuery ??= MockSystemQuery();
        systemCommand ??= new Mock<ISystemSettingsCommandService>();
        loggingRuntime ??= new Mock<ILoggingRuntimeApplier>();

        var loggingCommand = new Mock<ILoggingSettingsCommandService>();
        var statusColors = new Mock<IStatusColorSettingsService>();
        statusColors.Setup(s => s.GetUserStatusColorsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        statusColors.Setup(s => s.GetGlobalStatusColorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(
                AppFeatureCodes.SystemSettingsWrite,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(isAdmin);

        return new SettingsViewModel(
            appSettings.Object,
            systemQuery.Object,
            systemCommand.Object,
            loggingCommand.Object,
            loggingRuntime.Object,
            statusColors.Object,
            auth.Object,
            new StubCurrentUser(userId),
            scope);
    }

    private static Mock<IAppSettingsService> MockAppSettings(TaskCompletionSource? onSave = null)
    {
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonAppSettingsService.CreateDefaultDto());
        mock.Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns<UserAppSettingsDto, CancellationToken>((_, _) =>
            {
                onSave?.TrySetResult();
                return Task.CompletedTask;
            });
        return mock;
    }

    private static Mock<ISystemSettingsQueryService> MockSystemQuery()
    {
        var mock = new Mock<ISystemSettingsQueryService>();
        mock.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptySystemSettings());
        return mock;
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
