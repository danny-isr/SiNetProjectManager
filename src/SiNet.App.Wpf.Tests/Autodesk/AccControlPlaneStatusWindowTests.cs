using System.IO;
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
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
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
        Assert.Equal(["b.project-1", "b.project-2"], vm.KnownProjectIds);
    }

    [Fact]
    public void Status_window_view_uses_shared_acc_status_control()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Autodesk/AccControlPlaneStatusWindowView.xaml");

        Assert.Contains("AccControlPlaneStatusView", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectsSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("ResolveDocumentCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadLookupSeedCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("UseSelectedFileCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseFolders", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseFiles", xaml, StringComparison.Ordinal);
        Assert.Contains("KnownProjectIds", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedKnownProjectId", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseParentFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseTrailText", xaml, StringComparison.Ordinal);
        Assert.Contains("LookupProjectId", xaml, StringComparison.Ordinal);
        Assert.Contains("LookupResolvedDocsUrl, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("CopyResolvedDocsUrlCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenResolvedDocsUrlCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_loads_lookup_seed_from_db_hints()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            new StubAccLookupSeedService(
            [
                new AccDocumentLookupSeed("b.project-1", "folder-22", "drawing.pdf", "item-77", "EmailInboxAttachment 2026-07-03 23:00")
            ]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());

        await vm.LoadLookupSeedAsync();

        Assert.Equal("b.project-1", vm.LookupProjectId);
        Assert.Equal("folder-22", vm.LookupFolderId);
        Assert.Equal("drawing.pdf", vm.LookupFileName);
        Assert.Contains("נטענה דוגמה מה-DB", vm.LookupResultSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_uses_selected_known_project_id()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        {
            LookupFolderId = "old-folder",
            LookupFileName = "old-file.pdf",
        };

        await vm.LoadAsync();
        vm.SelectedKnownProjectId = "b.project-1";

        Assert.Equal("b.project-1", vm.LookupProjectId);
        Assert.Equal(string.Empty, vm.LookupFolderId);
        Assert.Equal(string.Empty, vm.LookupFileName);
        Assert.Equal("טרם נטען תוכן ACC.", vm.BrowseSummary);
    }

    [Fact]
    public async Task Status_window_view_model_browses_folders_and_files_from_project_files_root()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(null),
            new StubAccFolderBrowserService(new AccFolderBrowseResult(
                "b.project-1",
                "root-folder",
                [
                    new AccFolderBrowseEntry("folder-a", "A Folder", AccFolderEntryKind.Folder, 0, null, null),
                    new AccFolderBrowseEntry("item-b", "B File.pdf", AccFolderEntryKind.Item, 123, null, null),
                ])),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        {
            LookupProjectId = "b.project-1",
        };

        await vm.BrowseFolderAsync();

        Assert.Equal("root-folder", vm.LookupFolderId);
        Assert.Single(vm.BrowseFolders);
        Assert.Single(vm.BrowseFiles);
        Assert.Equal("A Folder", vm.BrowseFolders[0].DisplayName);
        Assert.Equal("B File.pdf", vm.BrowseFiles[0].DisplayName);

        vm.SelectedBrowseFile = vm.BrowseFiles[0];
        vm.UseSelectedFileCommand.Execute(null);
        Assert.Equal("B File.pdf", vm.LookupFileName);
    }

    [Fact]
    public async Task Status_window_view_model_can_navigate_back_to_parent_folder()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
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
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        {
            LookupProjectId = "b.project-1",
        };

        await vm.BrowseFolderAsync();
        await vm.OpenSelectedFolderAsync();

        Assert.Equal("folder-a", vm.LookupFolderId);
        Assert.Equal("Project Files / A Folder", vm.BrowseTrailText);
        Assert.True(vm.BrowseParentFolderCommand.CanExecute(null));

        await vm.BrowseParentFolderAsync();

        Assert.Equal("root-folder", vm.LookupFolderId);
        Assert.Equal("Project Files", vm.BrowseTrailText);
        Assert.False(vm.BrowseParentFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Status_window_view_model_enables_copy_and_open_for_live_docs_url()
    {
        var launcher = new StubAccResolvedDocsUrlLauncher();
        var clipboard = new StubClipboardTextWriter();
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(new AccItemRef("b.project-1", "item-77", "version-3", null)),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            new StubAccLookupSeedService([]),
            launcher,
            clipboard)
        {
            LookupProjectId = "b.project-1",
            LookupFolderId = "folder-22",
            LookupFileName = "drawing.pdf",
        };

        await vm.ResolveDocumentAsync();

        Assert.True(vm.CopyResolvedDocsUrlCommand.CanExecute(null));
        Assert.True(vm.OpenResolvedDocsUrlCommand.CanExecute(null));

        vm.CopyResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.LookupResolvedDocsUrl, clipboard.LastText);
        Assert.Contains("הועתק", vm.SummaryMessage, StringComparison.Ordinal);

        vm.OpenResolvedDocsUrlCommand.Execute(null);
        Assert.Equal(vm.LookupResolvedDocsUrl, launcher.LastOpenedUrl);
        Assert.Contains("נפתח בדפדפן", vm.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_resolves_document_lookup_summary()
    {
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(new AccItemRef("b.project-1", "item-77", "version-3", null)),
            new StubAccFolderBrowserService((AccFolderBrowseResult?)null),
            new StubAccLookupSeedService([]),
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter())
        {
            LookupProjectId = "b.project-1",
            LookupFolderId = "folder-22",
            LookupFileName = "drawing.pdf",
        };

        await vm.ResolveDocumentAsync();

        Assert.Contains("item-77", vm.LookupResultSummary, StringComparison.Ordinal);
        Assert.Contains("version-3", vm.LookupResultSummary, StringComparison.Ordinal);
        Assert.Equal(
            "https://acc.autodesk.com/docs/files/projects/project-1?folderUrn=folder-22&entityId=item-77",
            vm.LookupResolvedDocsUrl);
    }

    private static AccControlPlaneStatusPresenter BuildPresenter() =>
        new(
            new StubAccModeProvider(AccServiceMode.Local, null),
            new StubAccProjectService(["b.project-1"]),
            new StubAccKeyDiagnostics(new AccServiceKeyInfo(false, 0, null)),
            new StubAccHealthProbe(new AccServiceHealthResult(false, AccServiceHealthState.NotConfigured, null, "Not configured")),
            new StubAccDiagnosticsProbe(new AccServiceDiagnosticsResult(false, null, false, null, 0, null, false, "Not configured", false, "Not configured")));

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
