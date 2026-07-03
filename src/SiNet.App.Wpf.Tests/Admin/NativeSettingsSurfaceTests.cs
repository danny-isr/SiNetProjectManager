using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.Application.Abstractions.Autodesk;
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
        SystemSettingsDto? savedDto = null;
        systemCommand.Setup(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns<SystemSettingsDto, CancellationToken>((dto, _) =>
            {
                savedDto = dto;
                systemSaved.TrySetResult();
                return Task.CompletedTask;
            });

        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            appSettings,
            systemQuery: MockSystemQuery(CreateNonDefaultSystemSettings()),
            systemCommand: systemCommand);

        await vm.LoadAsync();
        vm.AccServiceBaseUrl = " https://acc.example.com/ ";
        vm.SaveCommand.Execute(null);
        await systemSaved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        systemCommand.Verify(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()), Times.Once);
        appSettings.Verify(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("https://acc.example.com", savedDto?.Acc.AccServiceBaseUrl);
        Assert.Contains("הפעלה מחדש", vm.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_load_populates_acc_runtime_status_panel()
    {
        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            accProjectService: MockAccProjectService(["b.project-1", "b.project-2"]),
            accModeProvider: MockAccModeProvider(AccServiceMode.Remote, "https://runtime-acc.example.com"),
            accKeyDiagnostics: MockAccKeyDiagnostics(new AccServiceKeyInfo(true, 44, "abc123def456")),
            accHealthProbe: MockAccHealthProbe(new AccServiceHealthResult(true, AccServiceHealthState.Online, "https://runtime-acc.example.com/v1/acc/health", "Connected")),
            accDiagnosticsProbe: MockAccDiagnosticsProbe(new AccServiceDiagnosticsResult(
                Reachable: true,
                WindowsUser: "DOMAIN\\runtime",
                HasApiKey: true,
                KeySource: "CredentialManager",
                KeyLength: 44,
                KeyHashPrefix: "abc123def456",
                AutodeskOk: true,
                AutodeskDetail: "Autodesk token retrieved successfully.",
                DbOk: true,
                DbDetail: "Database connection successful.")));

        await vm.LoadAsync();

        Assert.Contains("runtime-acc.example.com", vm.AccServiceRuntimeModeSummary, StringComparison.Ordinal);
        Assert.Contains("abc123def456", vm.AccServiceRuntimeKeySummary, StringComparison.Ordinal);
        Assert.Contains("b.project-1", vm.AccServiceRuntimeProjectsSummary, StringComparison.Ordinal);
        Assert.Equal(["b.project-1", "b.project-2"], vm.AccKnownProjectIds);
        Assert.Contains("/v1/acc/health", vm.AccServiceRuntimeHealthSummary, StringComparison.Ordinal);
        Assert.Contains("DOMAIN\\runtime", vm.AccServiceRuntimeDiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_scope_can_select_known_acc_project_and_browse_runtime_folder_contents()
    {
        var folderBrowserService = new Mock<IAccFolderBrowserService>();
        folderBrowserService
            .Setup(x => x.BrowseAsync("b.project-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccFolderBrowseResult(
                "b.project-1",
                "root-folder",
                [
                    new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("item-b", "B File.pdf", AccFolderEntryKind.Item, 123, null, null),
                ]));

        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            accProjectService: MockAccProjectService(["b.project-1", "b.project-2"]),
            accFolderBrowserService: folderBrowserService);

        await vm.LoadAsync();
        vm.SelectedAccKnownProjectId = "b.project-1";

        Assert.Equal("b.project-1", vm.AccLookupProjectId);
        Assert.Equal(string.Empty, vm.AccLookupFolderId);
        Assert.Equal(string.Empty, vm.AccLookupFileName);

        await vm.BrowseAccFolderAsync();

        Assert.Equal("root-folder", vm.AccLookupFolderId);
        Assert.Single(vm.AccBrowseFolders);
        Assert.Single(vm.AccBrowseFiles);
        Assert.Equal("Project Files", vm.AccBrowseTrailText);

        vm.SelectedAccBrowseFile = vm.AccBrowseFiles[0];
        vm.UseSelectedAccFileCommand.Execute(null);
        Assert.Equal("B File.pdf", vm.AccLookupFileName);
    }

    [Fact]
    public async Task System_scope_resolves_live_acc_document_and_enables_copy_open_actions()
    {
        var documentService = new Mock<IAccDocumentService>();
        documentService
            .Setup(x => x.FindItemAsync("b.project-1", "folder-22", "drawing.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccItemRef("b.project-1", "item-77", "version-3", null));

        var launcher = new StubAccResolvedDocsUrlLauncher();
        var clipboard = new StubClipboardTextWriter();
        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            accDocumentService: documentService,
            resolvedDocsUrlLauncher: launcher,
            clipboardTextWriter: clipboard);

        await vm.LoadAsync();
        vm.AccLookupProjectId = "b.project-1";
        vm.AccLookupFolderId = "folder-22";
        vm.AccLookupFileName = "drawing.pdf";

        await vm.ResolveAccDocumentAsync();

        Assert.Contains("item-77", vm.AccLookupResultSummary, StringComparison.Ordinal);
        Assert.Equal(
            "https://acc.autodesk.com/docs/files/projects/project-1?folderUrn=folder-22&entityId=item-77",
            vm.AccLookupResolvedDocsUrl);
        Assert.True(vm.CopyAccResolvedDocsUrlCommand.CanExecute(null));
        Assert.True(vm.OpenAccResolvedDocsUrlCommand.CanExecute(null));

        vm.CopyAccResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.AccLookupResolvedDocsUrl, clipboard.LastText);

        vm.OpenAccResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.AccLookupResolvedDocsUrl, launcher.LastOpenedUrl);
    }

    [Fact]
    public async Task System_save_rejects_invalid_acc_service_base_url()
    {
        var systemCommand = new Mock<ISystemSettingsCommandService>();
        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            systemCommand: systemCommand);

        await vm.LoadAsync();
        vm.AccServiceBaseUrl = "not-a-url";

        vm.SaveCommand.Execute(null);

        systemCommand.Verify(s => s.SaveSystemSettingsAsync(It.IsAny<SystemSettingsDto>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("URL תקינה", vm.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_xaml_shows_acc_runtime_status_panel()
    {
        var xaml = File.ReadAllText(Path.Combine(
            Boundary.RepoPaths.RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "Admin",
            "Settings",
            "SettingsView.xaml"));

        Assert.Contains("AccServiceRuntimeHint", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceRuntimeModeSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceRuntimeProjectsSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceRuntimeHealthSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceRuntimeDiagnosticsSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccReadOnlyDocumentBrowserView", xaml, StringComparison.Ordinal);
        Assert.Contains("DataContext=\"{Binding AccBrowser}\"", xaml, StringComparison.Ordinal);
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

    [Fact]
    public async Task Personal_load_applies_non_default_user_settings_to_view_model()
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNonDefaultUserSettings());

        var vm = CreateViewModel(
            isAdmin: false,
            userId: 1,
            SettingsSurfaceScope.Personal,
            appSettings: appSettings);

        await vm.LoadAsync();

        Assert.Equal("Arial", vm.FontFamily);
        Assert.Equal(18, vm.BaseFontSize);
        Assert.Equal("#FF0000", vm.ForegroundColor);
        Assert.True(vm.LoggingEnabled);
        Assert.Equal(@"D:\TestLogs", vm.LogDirectory);
    }

    [Fact]
    public async Task System_load_applies_non_default_system_settings_to_view_model()
    {
        var systemQuery = new Mock<ISystemSettingsQueryService>();
        systemQuery.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNonDefaultSystemSettings());

        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            systemQuery: systemQuery);

        await vm.LoadAsync();

        Assert.Equal("Custom Project Title", vm.DefaultProjectTitle);
        Assert.Equal("https://acc.example.com", vm.AccServiceBaseUrl);
        Assert.Equal(@"\\server\logs", vm.CentralLogPath);
        Assert.Equal(LogLevelDto.Debug.ToString(), vm.ClientFileLevel);
    }

    [Fact]
    public async Task Personal_load_raises_property_changed_for_bound_personal_fields()
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNonDefaultUserSettings());

        var vm = CreateViewModel(
            isAdmin: false,
            userId: 1,
            SettingsSurfaceScope.Personal,
            appSettings: appSettings);

        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        await vm.LoadAsync();

        Assert.Contains(nameof(SettingsViewModel.FontFamily), changed);
        Assert.Contains(nameof(SettingsViewModel.LoggingEnabled), changed);
    }

    [Fact]
    public async Task System_load_raises_property_changed_for_bound_system_fields()
    {
        var systemQuery = new Mock<ISystemSettingsQueryService>();
        systemQuery.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNonDefaultSystemSettings());

        var vm = CreateViewModel(
            isAdmin: true,
            userId: 1,
            SettingsSurfaceScope.SystemAdmin,
            systemQuery: systemQuery);

        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        await vm.LoadAsync();

        Assert.Contains(nameof(SettingsViewModel.DefaultProjectTitle), changed);
        Assert.Contains(nameof(SettingsViewModel.CentralLogPath), changed);
    }

    [Fact]
    public async Task Personal_save_without_logging_change_does_not_apply_runtime_logging()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonAppSettingsService.CreateDefaultDto());

        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();

        var vm = CreateViewModel(
            isAdmin: false,
            userId: 1,
            SettingsSurfaceScope.Personal,
            appSettings,
            loggingRuntime: loggingRuntime);

        await vm.LoadAsync();
        vm.FontFamily = "Tahoma";
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        loggingRuntime.Verify(r => r.ApplyUserLogging(It.IsAny<UserLoggingSettingsDto>()), Times.Never);
    }

    [Fact]
    public async Task Personal_save_with_logging_change_applies_runtime_logging()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonAppSettingsService.CreateDefaultDto());

        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();

        var vm = CreateViewModel(
            isAdmin: false,
            userId: 1,
            SettingsSurfaceScope.Personal,
            appSettings,
            loggingRuntime: loggingRuntime);

        await vm.LoadAsync();
        vm.LoggingEnabled = true;
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        loggingRuntime.Verify(r => r.ApplyUserLogging(It.Is<UserLoggingSettingsDto>(d => d.LoggingEnabled)), Times.Once);
    }

    private static UserAppSettingsDto CreateNonDefaultUserSettings()
    {
        var defaults = UserAppSettingsDefaults.Create();
        return defaults with
        {
            Appearance = TypographyThemeDefaults.CreateDefaultAppearance() with
            {
                FontFamily = "Arial",
                BaseFontSize = 18,
                ForegroundColor = "#FF0000",
                BackgroundColor = "#00FF00",
            },
            Logging = new UserLoggingSettingsDto(
                true,
                @"D:\TestLogs",
                LoggingSettingsMetadata.BootstrapDefaultLocalLogDirectory,
                LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory),
        };
    }

    private static SystemSettingsDto CreateNonDefaultSystemSettings()
    {
        var log = new CentralLoggingSettingsDto(
            @"\\server\logs",
            21,
            45,
            new AppLogLevelsDto(LogLevelDto.Debug, LogLevelDto.Information),
            new AppLogLevelsDto(LogLevelDto.Warning, LogLevelDto.Error),
            new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Error),
            true);

        return new SystemSettingsDto(
            new EmailOfficeSystemSettingsDto(
                "Custom Project Title",
                "99",
                "250",
                "CustomInbox",
                "Inbox Project",
                5),
            new AccSystemSettingsDto(
                "https://acc.example.com",
                "admin@example.com",
                "Template-X",
                ".pdf,.dwg"),
            new InspectionSystemSettingsDto("tpl-folder", "rpt-folder", "C:\\Reports", "C:\\stamp.png"),
            new InspectionStatusLabelsDto("OK", "Fail", "ReFail", "N/A"),
            new AiSystemSettingsDto(
                "http://ollama.local",
                "llama3",
                new AiModelLevelSelectionDto("simple-model", "openai"),
                new AiModelLevelSelectionDto("qc-model", "openai"),
                new AiModelLevelSelectionDto("write-model", "openai"),
                new AiModelLevelSelectionDto("deep-model", "openai"),
                "model-a,model-b"),
            log);
    }

    private static SettingsViewModel CreateViewModel(
        bool isAdmin,
        int userId,
        SettingsSurfaceScope scope,
        Mock<IAppSettingsService>? appSettings = null,
        Mock<ISystemSettingsQueryService>? systemQuery = null,
        Mock<ISystemSettingsCommandService>? systemCommand = null,
        Mock<ILoggingRuntimeApplier>? loggingRuntime = null,
        Mock<IThemeRuntimeApplier>? themeRuntime = null,
        Mock<IAccProjectService>? accProjectService = null,
        Mock<IAccServiceModeProvider>? accModeProvider = null,
        Mock<IAccServiceKeyDiagnostics>? accKeyDiagnostics = null,
        Mock<IAccServiceHealthProbe>? accHealthProbe = null,
        Mock<IAccServiceDiagnosticsProbe>? accDiagnosticsProbe = null,
        Mock<IAccProjectCatalogService>? accProjectCatalogService = null,
        Mock<IAccDocumentService>? accDocumentService = null,
        Mock<IAccFolderBrowserService>? accFolderBrowserService = null,
        IAccResolvedDocsUrlLauncher? resolvedDocsUrlLauncher = null,
        IClipboardTextWriter? clipboardTextWriter = null)
    {
        appSettings ??= MockAppSettings();
        systemQuery ??= MockSystemQuery();
        systemCommand ??= new Mock<ISystemSettingsCommandService>();
        loggingRuntime ??= new Mock<ILoggingRuntimeApplier>();
        themeRuntime ??= new Mock<IThemeRuntimeApplier>();
        accProjectService ??= MockAccProjectService([]);
        accModeProvider ??= MockAccModeProvider(AccServiceMode.Local, null);
        accKeyDiagnostics ??= MockAccKeyDiagnostics(new AccServiceKeyInfo(false, 0, null));
        accHealthProbe ??= MockAccHealthProbe(new AccServiceHealthResult(false, AccServiceHealthState.NotConfigured, null, "Not configured"));
        accDiagnosticsProbe ??= MockAccDiagnosticsProbe(new AccServiceDiagnosticsResult(false, null, false, null, 0, null, false, "Not configured", false, "Not configured"));
        accProjectCatalogService ??= MockAccProjectCatalogService([]);
        accDocumentService ??= new Mock<IAccDocumentService>();
        accFolderBrowserService ??= new Mock<IAccFolderBrowserService>();
        resolvedDocsUrlLauncher ??= new StubAccResolvedDocsUrlLauncher();
        clipboardTextWriter ??= new StubClipboardTextWriter();

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
            themeRuntime.Object,
            statusColors.Object,
            new AccControlPlaneStatusPresenter(
                accModeProvider.Object,
                accProjectService.Object,
                accKeyDiagnostics.Object,
                accHealthProbe.Object,
                accDiagnosticsProbe.Object),
            accProjectCatalogService.Object,
            accDocumentService.Object,
            accFolderBrowserService.Object,
            resolvedDocsUrlLauncher,
            clipboardTextWriter,
            auth.Object,
            new StubCurrentUser(userId),
            scope);
    }

    private static Mock<IAccServiceModeProvider> MockAccModeProvider(AccServiceMode mode, string? baseUrl)
    {
        var mock = new Mock<IAccServiceModeProvider>();
        mock.SetupGet(x => x.Mode).Returns(mode);
        mock.SetupGet(x => x.BaseUrl).Returns(baseUrl);
        return mock;
    }

    private static Mock<IAccProjectService> MockAccProjectService(IReadOnlyList<string> projectIds)
    {
        var mock = new Mock<IAccProjectService>();
        mock.Setup(x => x.GetProjectIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(projectIds);
        return mock;
    }

    private static Mock<IAccProjectCatalogService> MockAccProjectCatalogService(IReadOnlyList<string> projectIds)
    {
        var mock = new Mock<IAccProjectCatalogService>();
        mock.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectIds.Select(projectId => new AccProjectCatalogEntry(projectId, projectId, "ProjectAccMapping")).ToArray());
        return mock;
    }

    private static Mock<IAccServiceKeyDiagnostics> MockAccKeyDiagnostics(AccServiceKeyInfo info)
    {
        var mock = new Mock<IAccServiceKeyDiagnostics>();
        mock.Setup(x => x.Describe()).Returns(info);
        return mock;
    }

    private static Mock<IAccServiceHealthProbe> MockAccHealthProbe(AccServiceHealthResult result)
    {
        var mock = new Mock<IAccServiceHealthProbe>();
        mock.Setup(x => x.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return mock;
    }

    private static Mock<IAccServiceDiagnosticsProbe> MockAccDiagnosticsProbe(AccServiceDiagnosticsResult result)
    {
        var mock = new Mock<IAccServiceDiagnosticsProbe>();
        mock.Setup(x => x.ProbeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return mock;
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

    private static Mock<ISystemSettingsQueryService> MockSystemQuery(SystemSettingsDto dto)
    {
        var mock = new Mock<ISystemSettingsQueryService>();
        mock.Setup(s => s.GetSystemSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
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

    private sealed class StubAccResolvedDocsUrlLauncher : IAccResolvedDocsUrlLauncher
    {
        public string? LastOpenedUrl { get; private set; }

        public void Open(string url) => LastOpenedUrl = url;
    }

    private sealed class StubClipboardTextWriter : IClipboardTextWriter
    {
        public string? LastText { get; private set; }

        public void SetText(string text) => LastText = text;
    }
}
