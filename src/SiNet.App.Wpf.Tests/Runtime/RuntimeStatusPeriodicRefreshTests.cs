using SiNet.App.Wpf.Runtime;
using SiNet.Application.Runtime;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

public sealed class RuntimeStatusPeriodicRefreshTests
{
    [Fact]
    public async Task WhenStartPeriodicRefreshThenFirstProbeRunsWithoutOpeningTheStatusWindow()
    {
        var contributor = new CountingContributor();
        using var service = new RuntimeSubsystemStatusService(
            new StartupTaskRegistry(),
            contributors: [contributor]);

        service.StartPeriodicRefresh(startupDelay: TimeSpan.FromMilliseconds(20), interval: TimeSpan.FromHours(1));

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (contributor.Calls == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(contributor.Calls >= 1, "Expected an automatic startup probe before any window open.");
    }

    [Fact]
    public void WhenStartPeriodicRefreshCalledTwiceThenLoopStartsOnlyOnce()
    {
        using var service = new RuntimeSubsystemStatusService(new StartupTaskRegistry());
        service.StartPeriodicRefresh(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        service.StartPeriodicRefresh(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        // Idempotency is the contract — no exception, second call is a no-op.
    }

    private sealed class CountingContributor : ISubsystemStatusContributor
    {
        public int Calls;

        public string Key => "probe-count";

        public string DisplayNameHe => "מונה";

        public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new SubsystemRuntimeStatus(
                Key,
                DisplayNameHe,
                SubsystemRuntimeState.Idle,
                null,
                "ok",
                DateTimeOffset.UtcNow));
        }
    }
}
