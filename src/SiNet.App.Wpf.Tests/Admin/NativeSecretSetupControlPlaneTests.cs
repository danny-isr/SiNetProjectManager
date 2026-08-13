using System.IO;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Admin.Security;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeSecretSetupControlPlaneTests
{
    [Fact]
    public async Task LoadAsync_populates_remote_acc_control_plane_panel()
    {
        var vm = CreateViewModel(
            new StubAccModeProvider(AccServiceMode.Remote, "https://acc.example.com"),
            new StubAccProjectService(["b.project-1", "b.project-2"]),
            new StubAccKeyDiagnostics(new AccServiceKeyInfo(true, 44, "abc123def456")),
            new StubAccHealthProbe(new AccServiceHealthResult(true, AccServiceHealthState.Online, "https://acc.example.com/v1/acc/health", "Connected")),
            new StubAccDiagnosticsProbe(new AccServiceDiagnosticsResult(
                Reachable: true,
                WindowsUser: "DOMAIN\\user",
                HasApiKey: true,
                KeySource: "CredentialManager",
                KeyLength: 44,
                KeyHashPrefix: "abc123def456",
                AutodeskOk: true,
                AutodeskDetail: "Autodesk token retrieved successfully.",
                DbOk: false,
                DbDetail: "DB failed")));

        await vm.LoadAsync();

        Assert.Contains("https://acc.example.com", vm.AccServiceModeSummary, StringComparison.Ordinal);
        Assert.Contains("abc123def456", vm.AccServiceKeySummary, StringComparison.Ordinal);
        Assert.Contains("b.project-1", vm.AccServiceProjectsSummary, StringComparison.Ordinal);
        Assert.Contains("/v1/acc/health", vm.AccServiceHealthSummary, StringComparison.Ordinal);
        Assert.Contains("DOMAIN\\user", vm.AccServiceDiagnosticsSummary, StringComparison.Ordinal);
        Assert.Contains("CredentialManager", vm.AccServiceDiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_uses_local_control_plane_text_without_network_calls()
    {
        var healthProbe = new CountingAccHealthProbe();
        var diagnosticsProbe = new CountingAccDiagnosticsProbe();
        var vm = CreateViewModel(
            new StubAccModeProvider(AccServiceMode.Local, null),
            new StubAccProjectService([]),
            new StubAccKeyDiagnostics(new AccServiceKeyInfo(false, 0, null)),
            healthProbe,
            diagnosticsProbe);

        await vm.LoadAsync();

        Assert.Contains("מקומי", vm.AccServiceModeSummary, StringComparison.Ordinal);
        Assert.Contains("לא הוגדר", vm.AccServiceKeySummary, StringComparison.Ordinal);
        Assert.Contains("לא נמצאו", vm.AccServiceProjectsSummary, StringComparison.Ordinal);
        Assert.Contains("לא רלוונטי", vm.AccServiceHealthSummary, StringComparison.Ordinal);
        Assert.Contains("/v1/acc/diag", vm.AccServiceDiagnosticsSummary, StringComparison.Ordinal);
        Assert.Equal(0, healthProbe.CallCount);
        Assert.Equal(0, diagnosticsProbe.CallCount);
    }

    [Fact]
    public void SecretSetupView_shows_acc_control_plane_bindings()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Security/SecretSetupView.xaml");

        Assert.Contains("AccServiceModeSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceKeySummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceProjectsSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceHealthSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("AccServiceDiagnosticsSummary", xaml, StringComparison.Ordinal);
    }

    private static SecretSetupViewModel CreateViewModel(
        IAccServiceModeProvider modeProvider,
        IAccProjectService projectService,
        IAccServiceKeyDiagnostics keyDiagnostics,
        IAccServiceHealthProbe healthProbe,
        IAccServiceDiagnosticsProbe diagnosticsProbe) =>
        new(
            new StubSecretSetupService(),
            new AccControlPlaneStatusPresenter(
                modeProvider,
                projectService,
                keyDiagnostics,
                healthProbe,
                diagnosticsProbe));

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

    private sealed class StubSecretSetupService : ISecretSetupService
    {
        public Task<IReadOnlyList<SecretStatusDto>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretStatusDto>>(
                SecretCatalog.All
                    .Select(entry => new SecretStatusDto(entry.Key, SecretStatusLevel.Missing, null, "missing"))
                    .ToArray());

        public Task<SecretSetupSnapshotDto> GetEditableSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretSetupSnapshotDto(
                new Dictionary<string, string?>(),
                new Dictionary<string, bool>(),
                "Not configured"));

        public Task<SecretSaveResultDto> SaveAndValidateAsync(SecretSetupUpdateDto update, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretSaveResultDto(0, [], false, [], []));

        public Task<SecretExportResultDto> ExportAsync(string filePath, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretImportPreviewDto> PreviewImportAsync(string filePath, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SecretImportResultDto> ImportAsync(string filePath, string password, SecretImportMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GenerateAccServiceApiKeyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GenerateAccServiceCertificatePasswordAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccServiceDiagnosticResultDto> TestAccServiceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private sealed class CountingAccHealthProbe : IAccServiceHealthProbe
    {
        public int CallCount { get; private set; }

        public Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AccServiceHealthResult(true, AccServiceHealthState.Offline, null, "should not call"));
        }
    }

    private sealed class StubAccDiagnosticsProbe(AccServiceDiagnosticsResult result) : IAccServiceDiagnosticsProbe
    {
        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class CountingAccDiagnosticsProbe : IAccServiceDiagnosticsProbe
    {
        public int CallCount { get; private set; }

        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AccServiceDiagnosticsResult(false, null, false, null, 0, null, false, "should not call", false, "should not call"));
        }
    }
}
