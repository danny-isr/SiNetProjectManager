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
            new StubAccResolvedDocsUrlLauncher(),
            new StubClipboardTextWriter());
        await vm.LoadAsync();

        Assert.Contains("מצב הריצה הנוכחי", vm.HintText, StringComparison.Ordinal);
        Assert.Contains("acc.example.com", vm.ModeSummary, StringComparison.Ordinal);
        Assert.Contains("abc123def456", vm.KeySummary, StringComparison.Ordinal);
        Assert.Contains("b.project-1", vm.ProjectsSummary, StringComparison.Ordinal);
        Assert.Contains("/v1/acc/health", vm.HealthSummary, StringComparison.Ordinal);
        Assert.Contains("DOMAIN\\acc", vm.DiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_window_view_uses_shared_acc_status_control()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Autodesk/AccControlPlaneStatusWindowView.xaml");

        Assert.Contains("AccControlPlaneStatusView", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectsSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("ResolveDocumentCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LookupProjectId", xaml, StringComparison.Ordinal);
        Assert.Contains("LookupResolvedDocsUrl", xaml, StringComparison.Ordinal);
        Assert.Contains("CopyResolvedDocsUrlCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenResolvedDocsUrlCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_window_view_model_enables_copy_and_open_for_live_docs_url()
    {
        var launcher = new StubAccResolvedDocsUrlLauncher();
        var clipboard = new StubClipboardTextWriter();
        var vm = new AccControlPlaneStatusWindowViewModel(
            BuildPresenter(),
            new StubAccDocumentService(new AccItemRef("b.project-1", "item-77", "version-3", null)),
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
