using System.IO;
using SiNet.App.Wpf.Runtime;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Runtime;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

public sealed class AccServiceStatusContributorDev027Tests
{
    [Fact]
    public async Task Fast_cycle_does_not_call_diag()
    {
        var probe = new StubHealth(new AccServiceHealthResult(true, AccServiceHealthState.Online, "https://x", "Connected"));
        var keys = new StubKeys(new AccServiceKeyInfo(true, 8, "abc"));
        var diag = new CountingDiag();
        var sut = new AccServiceStatusContributor(probe, keys, diag);

        var row = await sut.ContributeAsync(new SubsystemProbeContext(IncludeDeep: false));

        Assert.Equal(0, diag.Calls);
        Assert.Equal(SubsystemRuntimeState.Idle, row.State);
    }

    [Fact]
    public async Task Deep_cycle_maps_401_without_tls_summary()
    {
        var probe = new StubHealth(new AccServiceHealthResult(true, AccServiceHealthState.Online, "https://x", "Connected"));
        var keys = new StubKeys(new AccServiceKeyInfo(true, 8, "abc"));
        var diag = new StubDiag(new AccServiceDiagnosticsResult(
            Reachable: false,
            WindowsUser: null,
            HasApiKey: true,
            KeySource: null,
            KeyLength: 8,
            KeyHashPrefix: null,
            AutodeskOk: false,
            AutodeskDetail: "HTTP 401 — client API key rejected by server.",
            DbOk: false,
            DbDetail: "HTTP 401 — client API key rejected by server."));
        var sut = new AccServiceStatusContributor(probe, keys, diag);

        var row = await sut.ContributeAsync(new SubsystemProbeContext(IncludeDeep: true));

        Assert.Equal(SubsystemRuntimeState.Degraded, row.State);
        Assert.Contains("401", row.SummaryHe, StringComparison.Ordinal);
        Assert.DoesNotContain("SSL", row.SummaryHe, StringComparison.OrdinalIgnoreCase);

        var guidance = SystemStatusGuidanceCatalog.Resolve(row.Key, row.State, row.SummaryHe);
        Assert.Contains("ייבוא מפתחות תחנה", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fast_cycle_skips_deep_tier_contributor()
    {
        var deep = new DeepCountingContributor();
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [deep]);

        await service.RefreshAsync(includeDeep: false);

        Assert.Equal(0, deep.Calls);
        var row = Assert.Single(service.Current, s => s.Key == "masterplan-replica");
        Assert.Contains("רענון עמוק", row.SummaryHe, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deep_cycle_runs_deep_tier_contributor()
    {
        var deep = new DeepCountingContributor();
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [deep]);

        await service.RefreshAsync(includeDeep: true);

        Assert.Equal(1, deep.Calls);
    }

    [Fact]
    public void Gmail_list_does_not_log_error_on_operation_canceled()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");
        Assert.Contains("when (ex is not OperationCanceledException)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubHealth(AccServiceHealthResult result) : IAccServiceHealthProbe
    {
        public Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class StubKeys(AccServiceKeyInfo info) : IAccServiceKeyDiagnostics
    {
        public AccServiceKeyInfo Describe() => info;
    }

    private sealed class CountingDiag : IAccServiceDiagnosticsProbe
    {
        public int Calls;

        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new AccServiceDiagnosticsResult(
                true, "u", true, "vault", 8, "ab", true, "ok", true, "ok"));
        }
    }

    private sealed class StubDiag(AccServiceDiagnosticsResult result) : IAccServiceDiagnosticsProbe
    {
        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class DeepCountingContributor : ISubsystemStatusContributor
    {
        public int Calls;
        public string Key => "masterplan-replica";
        public string DisplayNameHe => "רפליקה";
        public SubsystemProbeTier Tier => SubsystemProbeTier.Deep;

        public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new SubsystemRuntimeStatus(
                Key, DisplayNameHe, SubsystemRuntimeState.Idle, null, "ok", DateTimeOffset.UtcNow));
        }
    }
}
