using System.Diagnostics;
using SiNet.App.Wpf.Runtime;
using SiNet.Application.Runtime;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

/// <summary>
/// Guards the contributor merge added for <c>docs/SYSTEM_HEALTH.md</c> §2.2.
/// <para>
/// The aggregator is shared by the standalone host and the V2 hybrid host. V2 registers the legacy
/// bridge (<see cref="IExternalHealthCheckSource"/>) AND would resolve the same contributors, so the
/// merge must be first-wins with the bridge ordered first — otherwise V2 users see duplicate rows.
/// </para>
/// </summary>
public sealed class SubsystemStatusContributorMergeTests
{
    [Fact]
    public async Task WhenNoLegacyBridgeThenContributorRowIsShown()
    {
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [Contributor("database", "מסד נתונים", SubsystemRuntimeState.Idle, "מחובר")]);

        await service.RefreshAsync();

        var row = Assert.Single(service.Current, s => s.Key == "database");
        Assert.Equal(SubsystemRuntimeState.Idle, row.State);
        Assert.Equal("מחובר", row.SummaryHe);
    }

    [Fact]
    public async Task WhenLegacyBridgeCoversSameKeyThenBridgeWinsAndRowIsNotDuplicated()
    {
        var bridge = new StubExternalHealth(
        [
            new ExternalHealthCheckSnapshot(
                "database",
                "מסד נתונים",
                SubsystemRuntimeState.Idle,
                "legacy",
                DateTimeOffset.UtcNow),
        ]);

        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            externalHealth: bridge,
            contributors: [Contributor("database", "מסד נתונים", SubsystemRuntimeState.Degraded, "contributor")]);

        await service.RefreshAsync();

        var row = Assert.Single(service.Current, s => s.Key == "database");
        Assert.Equal("legacy", row.SummaryHe);
    }

    [Fact]
    public async Task WhenContributorThrowsThenItYieldsDegradedRowAndOtherRowsSurvive()
    {
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors:
            [
                new ThrowingContributor("file-server", "שרת קבצים"),
                Contributor("database", "מסד נתונים", SubsystemRuntimeState.Idle, "מחובר"),
            ]);

        await service.RefreshAsync();

        var failed = Assert.Single(service.Current, s => s.Key == "file-server");
        Assert.Equal(SubsystemRuntimeState.Degraded, failed.State);
        Assert.Contains("boom", failed.SummaryHe, StringComparison.Ordinal);

        var healthy = Assert.Single(service.Current, s => s.Key == "database");
        Assert.Equal(SubsystemRuntimeState.Idle, healthy.State);
    }

    [Fact]
    public async Task WhenContributorHangsThenItTimesOutInsteadOfStallingThePanel()
    {
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [new HangingContributor("ollama", "שרת AI")],
            contributorTimeout: TimeSpan.FromMilliseconds(100));

        await service.RefreshAsync();

        var row = Assert.Single(service.Current, s => s.Key == "ollama");
        Assert.Equal(SubsystemRuntimeState.Degraded, row.State);
    }

    [Fact]
    public async Task WhenContributorUsesBuiltInKeyThenBuiltInRowIsNotDuplicated()
    {
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [Contributor("gmail", "Gmail / Google", SubsystemRuntimeState.Idle, "contributor")]);

        await service.RefreshAsync();

        Assert.Single(service.Current, s => s.Key == "gmail");
    }

    private static ISubsystemStatusContributor Contributor(
        string key,
        string displayName,
        SubsystemRuntimeState state,
        string summary) => new StubContributor(key, displayName, state, summary);

    private sealed class StubContributor(
        string key,
        string displayNameHe,
        SubsystemRuntimeState state,
        string summaryHe) : ISubsystemStatusContributor
    {
        public string Key { get; } = key;

        public string DisplayNameHe { get; } = displayNameHe;

        public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                state,
                ActiveWorkCount: null,
                summaryHe,
                DateTimeOffset.UtcNow));
    }

    private sealed class ThrowingContributor(string key, string displayNameHe) : ISubsystemStatusContributor
    {
        public string Key { get; } = key;

        public string DisplayNameHe { get; } = displayNameHe;

        public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class HangingContributor(string key, string displayNameHe) : ISubsystemStatusContributor
    {
        public string Key { get; } = key;

        public string DisplayNameHe { get; } = displayNameHe;

        public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private sealed class StubExternalHealth(IReadOnlyList<ExternalHealthCheckSnapshot> current)
        : IExternalHealthCheckSource
    {
        public IReadOnlyList<ExternalHealthCheckSnapshot> Current { get; } = current;

        public event EventHandler? Changed;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
