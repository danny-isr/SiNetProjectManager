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

        var vm = new AccControlPlaneStatusWindowViewModel(presenter);
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
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    }

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
