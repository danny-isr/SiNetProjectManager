using System.IO;
using System.Linq;
using SiNet.App.Wpf.Autodesk;
using SiNet.Application.Abstractions.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccControlPlaneStatusWindowTests
{
    [Fact]
    public async Task Status_window_view_model_loads_runtime_snapshot()
    {
        var presenter = new AccControlPlaneStatusPresenter(
            new StubAccModeProvider(AccServiceMode.Remote, "https://acc.example.com"),
            new StubAccProjectService(["b.project-1", "b.project-2"]),
            new StubAccKeyDiagnostics(new AccServiceKeyInfo(true, 44, "abc123def456")),
            new StubAccHealthProbe(new AccServiceHealthResult(true, AccServiceHealthState.Online, "https://acc.example.com/v1/acc/health", "Connected")),
            new StubAccDiagnosticsProbe(new AccServiceDiagnosticsResult(
                Reachable: true,
                WindowsUser: "DOMAIN\\acc",
                HasApiKey: true,
                KeySource: "CredentialManager",
                KeyLength: 44,
                KeyHashPrefix: "abc123def456",
                AutodeskOk: true,
                AutodeskDetail: "Autodesk token retrieved successfully.",
                DbOk: true,
                DbDetail: "Database connection successful.")));

        var vm = new AccControlPlaneStatusWindowViewModel(
            presenter,
            BuildCatalogService("b.project-1", "b.project-2"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());
        await vm.LoadAsync();

        Assert.Contains("מצב הריצה הנוכחי", vm.HintText, StringComparison.Ordinal);
        Assert.Contains("acc.example.com", vm.ModeSummary, StringComparison.Ordinal);
        Assert.Contains("abc123def456", vm.KeySummary, StringComparison.Ordinal);
        Assert.Contains("b.project-1", vm.ProjectsSummary, StringComparison.Ordinal);
        Assert.Contains("/v1/acc/health", vm.HealthSummary, StringComparison.Ordinal);
        Assert.Contains("DOMAIN\\acc", vm.DiagnosticsSummary, StringComparison.Ordinal);
        Assert.Equal(["b.project-1", "b.project-2"], vm.Browser.KnownProjectIds);
    }

    [Fact]
    public void Status_window_view_uses_shared_acc_status_control()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Autodesk/AccControlPlaneStatusWindowView.xaml");

        Assert.Contains("AccControlPlaneStatusView", xaml, StringComparison.Ordinal);
        Assert.Contains("AccReadOnlyDocumentBrowserView", xaml, StringComparison.Ordinal);
        Assert.Contains("DataContext=\"{Binding Browser}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboBox ItemsSource=\"{Binding KnownProjectIds}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadLookupSeedCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_acc_browser_view_exposes_tree_search_actions()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Autodesk/AccReadOnlyDocumentBrowserView.xaml");

        Assert.Contains("SearchProjectTreeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("UseSelectedSearchResultCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SearchResults", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_loads_lookup_seed_from_db_hints()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService(
            [
                new AccDocumentLookupSeed("b.project-1", "folder-22", "drawing.pdf", "item-77", "EmailInboxAttachment 2026-07-03 23:00")
            ]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());

        await vm.LoadLookupSeedAsync();

        Assert.Equal("b.project-1", vm.Browser.LookupProjectId);
        Assert.Equal("folder-22", vm.Browser.LookupFolderId);
        Assert.Equal("drawing.pdf", vm.Browser.LookupFileName);
        Assert.Contains("נטענה דוגמה מה-DB", vm.Browser.LookupResultSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_uses_selected_known_project_id()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        {
        };
        vm.Browser.LookupFolderId = "old-folder";
        vm.Browser.LookupFileName = "old-file.pdf";

        await vm.LoadAsync();
        vm.Browser.SelectedKnownProjectId = "b.project-1";

        Assert.Equal("b.project-1", vm.Browser.LookupProjectId);
        Assert.Equal(string.Empty, vm.Browser.LookupFolderId);
        Assert.Equal(string.Empty, vm.Browser.LookupFileName);
        Assert.Equal("טרם נטען תוכן ACC.", vm.Browser.BrowseSummary);
    }

    [Fact]
    public async Task Status_window_view_model_browses_folders_and_files_from_project_files_root()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService(new AccFolderBrowseResult(
                "b.project-1",
                "root-folder",
                [
                    new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("item-b", "B File.pdf", AccFolderEntryKind.Item, 123, null, null),
                ])),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        ;
        vm.Browser.LookupProjectId = "b.project-1";

        await vm.Browser.BrowseFolderAsync();

        Assert.Equal("root-folder", vm.Browser.LookupFolderId);
        Assert.Single(vm.Browser.BrowseFolders);
        Assert.Single(vm.Browser.BrowseFiles);
        Assert.Equal("A Folder", vm.Browser.BrowseFolders[0].DisplayName);
        Assert.Equal("B File.pdf", vm.Browser.BrowseFiles[0].DisplayName);

        vm.Browser.SelectedBrowseFile = vm.Browser.BrowseFiles[0];
        vm.Browser.UseSelectedFileCommand.Execute(null);
        Assert.Equal("B File.pdf", vm.Browser.LookupFileName);
    }

    [Fact]
    public async Task Status_window_view_model_can_navigate_back_to_parent_folder()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((_, folderId) =>
            {
                var normalizedFolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
                return normalizedFolderId switch
                {
                    null or "root-folder" => new AccFolderBrowseResult(
                        "b.project-1",
                        "root-folder",
                        [
                            new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                        ]),
                    "folder-a" => new AccFolderBrowseResult(
                        "b.project-1",
                        "folder-a",
                        [
                            new AccFolderBrowseEntry("item-b", "B File.pdf", AccFolderEntryKind.Item, 123, null, null),
                        ]),
                    _ => null,
                };
            }),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        ;
        vm.Browser.LookupProjectId = "b.project-1";

        await vm.Browser.BrowseFolderAsync();
        await vm.Browser.OpenSelectedFolderAsync();

        Assert.Equal("folder-a", vm.Browser.LookupFolderId);
        Assert.Equal("Project Files / A Folder", vm.Browser.BrowseTrailText);
        Assert.True(vm.Browser.BrowseParentFolderCommand.CanExecute(null));

        await vm.Browser.BrowseParentFolderAsync();

        Assert.Equal("root-folder", vm.Browser.LookupFolderId);
        Assert.Equal("Project Files", vm.Browser.BrowseTrailText);
        Assert.False(vm.Browser.BrowseParentFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Status_window_view_model_enables_copy_and_open_for_live_docs_url()
    {
        var launcher = new StubAccResolvedDocsUrlLauncher();
        var clipboard = new StubClipboardTextWriter();
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(new AccItemRef("b.project-1", "item-77", "version-3", null)),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            launcher,
            clipboard);
        vm.Browser.LookupProjectId = "b.project-1";
        vm.Browser.LookupFolderId = "folder-22";
        vm.Browser.LookupFileName = "drawing.pdf";

        await vm.Browser.ResolveDocumentAsync();

        Assert.True(vm.Browser.CopyResolvedDocsUrlCommand.CanExecute(null));
        Assert.True(vm.Browser.OpenResolvedDocsUrlCommand.CanExecute(null));

        vm.Browser.CopyResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.Browser.LookupResolvedDocsUrl, clipboard.LastText);
        Assert.Contains("הועתק", vm.SummaryMessage, StringComparison.Ordinal);

        vm.Browser.OpenResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.Browser.LookupResolvedDocsUrl, launcher.LastOpenedUrl);
        Assert.Contains("נפתח בדפדפן", vm.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_resolves_document_lookup_summary()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(new AccItemRef("b.project-1", "item-77", "version-3", null)),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());
        vm.Browser.LookupProjectId = "b.project-1";
        vm.Browser.LookupFolderId = "folder-22";
        vm.Browser.LookupFileName = "drawing.pdf";

        await vm.Browser.ResolveDocumentAsync();

        Assert.Contains("item-77", vm.Browser.LookupResultSummary, StringComparison.Ordinal);
        Assert.Contains("version-3", vm.Browser.LookupResultSummary, StringComparison.Ordinal);
        Assert.Equal(
            "https://acc.autodesk.com/docs/files/projects/project-1?folderUrn=folder-22&entityId=item-77",
            vm.Browser.LookupResolvedDocsUrl);
    }

    [Fact]
    public async Task Status_window_view_model_can_load_live_hubs_projects_and_use_selected_project()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService(new AccFolderBrowseResult(
                "b.live-2",
                "root-folder",
                [
                    new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                ])),
            new StubAccLiveProjectDiscoveryService(
                [new AccHubCatalogEntry("b.hub-1", "Primary Hub", "EMEA")],
                [new AccProjectCatalogEntry("b.live-2", "Live Tower", "LiveAcc")]),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());

        await vm.Browser.LoadLiveHubsAsync();
        await vm.Browser.LoadLiveProjectsAsync();
        vm.Browser.UseSelectedLiveProjectCommand.Execute(null);
        await Task.Yield();

        Assert.Equal("b.live-2", vm.Browser.LookupProjectId);
        Assert.Equal("root-folder", vm.Browser.LookupFolderId);
        Assert.Equal("Live Tower", vm.Browser.SelectedKnownProject?.DisplayName);
        Assert.Single(vm.Browser.BrowseFolders);
        Assert.Contains("Project Files", vm.Browser.BrowseTrailText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_filters_browse_folders_and_files()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService(new AccFolderBrowseResult(
                "b.project-1",
                "root-folder",
                [
                    new AccFolderBrowseEntry("folder-a", "Architecture", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("folder-b", "Electrical", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("item-a", "Architectural Plan.pdf", AccFolderEntryKind.Item, 10, null, null),
                    new AccFolderBrowseEntry("item-b", "Electrical Notes.pdf", AccFolderEntryKind.Item, 20, null, null),
                ])),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());
        vm.Browser.LookupProjectId = "b.project-1";

        await vm.Browser.BrowseFolderAsync();
        vm.Browser.BrowseFolderFilterText = "arch";
        vm.Browser.BrowseFileFilterText = "notes";

        Assert.Single(vm.Browser.BrowseFolders);
        Assert.Equal("Architecture", vm.Browser.BrowseFolders[0].DisplayName);
        Assert.Single(vm.Browser.BrowseFiles);
        Assert.Equal("Electrical Notes.pdf", vm.Browser.BrowseFiles[0].DisplayName);
        Assert.Contains("אחרי סינון", vm.Browser.BrowseSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_can_search_project_tree_and_use_result()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            BuildCatalogService("b.project-1"),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((projectId, folderId) =>
            {
                var normalizedFolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
                return normalizedFolderId switch
                {
                    null => new AccFolderBrowseResult(
                        projectId,
                        "root-folder",
                        [
                            new AccFolderBrowseEntry("folder-a", "Discipline A", AccFolderEntryKind.Folder, 0, null, null),
                            new AccFolderBrowseEntry("folder-b", "Discipline B", AccFolderEntryKind.Folder, 0, null, null),
                        ]),
                    "folder-a" => new AccFolderBrowseResult(
                        projectId,
                        "folder-a",
                        [
                            new AccFolderBrowseEntry("folder-a1", "Sheets", AccFolderEntryKind.Folder, 0, null, null),
                        ]),
                    "folder-a1" => new AccFolderBrowseResult(
                        projectId,
                        "folder-a1",
                        [
                            new AccFolderBrowseEntry("item-1", "Tower Plan.pdf", AccFolderEntryKind.Item, 10, null, null),
                        ]),
                    "folder-b" => new AccFolderBrowseResult(
                        projectId,
                        "folder-b",
                        [
                            new AccFolderBrowseEntry("item-2", "Other Notes.pdf", AccFolderEntryKind.Item, 10, null, null),
                        ]),
                    _ => null,
                };
            }),
            BuildLiveDiscoveryService(),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());
        vm.Browser.LookupProjectId = "b.project-1";
        vm.Browser.LookupFileName = "plan";

        await vm.Browser.SearchProjectTreeAsync();

        Assert.Single(vm.Browser.SearchResults);
        Assert.Equal("folder-a1", vm.Browser.SearchResults[0].FolderId);
        Assert.Contains("Discipline A / Sheets", vm.Browser.SearchResults[0].FolderPath, StringComparison.Ordinal);
        Assert.Contains("נמצאו 1 קבצים", vm.Browser.TreeSearchSummary, StringComparison.Ordinal);

        vm.Browser.UseSelectedSearchResultCommand.Execute(null);

        Assert.Equal("folder-a1", vm.Browser.LookupFolderId);
        Assert.Equal("Tower Plan.pdf", vm.Browser.LookupFileName);
        Assert.Contains("Tower Plan.pdf", vm.Browser.LookupResultSummary, StringComparison.Ordinal);
    }

    private static AccControlPlaneStatusPresenter BuildPresenter() =>
        new(
            new StubAccModeProvider(AccServiceMode.Local, null),
            new StubAccProjectService(["b.project-1"]),
            new StubAccKeyDiagnostics(new AccServiceKeyInfo(false, 0, null)),
            new StubAccHealthProbe(new AccServiceHealthResult(false, AccServiceHealthState.NotConfigured, null, "Not configured")),
            new StubAccDiagnosticsProbe(new AccServiceDiagnosticsResult(false, null, false, null, 0, null, false, "Not configured", false, "Not configured")));

    private static StubAccProjectCatalogService BuildCatalogService(params string[] projectIds) =>
        new(projectIds
            .Select(projectId => new AccProjectCatalogEntry(projectId, $"Project {projectId[^1]}", "ProjectAccMapping"))
            .ToArray());

    private static StubAccLiveProjectDiscoveryService BuildLiveDiscoveryService() =>
        new([], []);

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubAccModeProvider(AccServiceMode mode, string? baseUrl) : IAccServiceModeProvider
    {
        public AccServiceMode Mode { get; } = mode;

        public string? BaseUrl { get; } = baseUrl;
    }

    private sealed class StubAccKeyDiagnostics(AccServiceKeyInfo result) : IAccServiceKeyDiagnostics
    {
        public AccServiceKeyInfo Describe() => result;
    }

    private sealed class StubAccProjectService(IReadOnlyList<string> projectIds) : IAccProjectService
    {
        public Task<IReadOnlyList<string>> GetProjectIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(projectIds);
    }

    private sealed class StubAccProjectCatalogService(IReadOnlyList<AccProjectCatalogEntry> projects) : IAccProjectCatalogService
    {
        public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(projects);
    }

    private sealed class StubAccLiveProjectDiscoveryService(
        IReadOnlyList<AccHubCatalogEntry> hubs,
        IReadOnlyList<AccProjectCatalogEntry> projects) : IAccLiveProjectDiscoveryService
    {
        public Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(hubs);

        public Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(string hubId, CancellationToken cancellationToken = default) =>
            Task.FromResult(projects);
    }

    private sealed class StubAccDocumentService(AccItemRef? result) : IAccDocumentService
    {
        public Task<AccItemRef?> FindItemAsync(
            string projectId,
            string folderId,
            string fileName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAccFolderBrowserService : IAccFolderBrowserService
    {
        private readonly Func<string, string?, AccFolderBrowseResult?> _handler;

        public StubAccFolderBrowserService(AccFolderBrowseResult? result)
            : this((_, _) => result)
        {
        }

        public StubAccFolderBrowserService(Func<string, string?, AccFolderBrowseResult?> handler)
        {
            _handler = handler;
        }

        public Task<AccFolderBrowseResult?> BrowseAsync(string projectId, string? folderId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_handler(projectId, folderId));
    }

    private sealed class StubAccLookupSeedService(IReadOnlyList<AccDocumentLookupSeed> seeds) : IAccLookupSeedService
    {
        public Task<IReadOnlyList<AccDocumentLookupSeed>> GetRecentSeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(seeds);
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

    private sealed class StubAccHealthProbe(AccServiceHealthResult result) : IAccServiceHealthProbe
    {
        public Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAccDiagnosticsProbe(AccServiceDiagnosticsResult result) : IAccServiceDiagnosticsProbe
    {
        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
