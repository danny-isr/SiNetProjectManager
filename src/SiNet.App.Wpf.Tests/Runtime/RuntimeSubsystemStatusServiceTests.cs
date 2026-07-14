using SiNet.App.Wpf.Runtime;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Common;
using SiNet.Application.Email.Acc;
using SiNet.Application.Runtime;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

public sealed class RuntimeSubsystemStatusServiceTests
{
    [Fact]
    public void Acc_ingest_is_Running_when_background_tracker_has_active_work()
    {
        var registry = new StartupTaskRegistry();
        var tracker = new StubAccIngestTracker(activeCount: 2);
        var health = new StubExternalHealth(
        [
            new ExternalHealthCheckSnapshot(
                "database",
                "מסד נתונים",
                SubsystemRuntimeState.Idle,
                "תקין",
                DateTimeOffset.UtcNow),
        ]);

        using var service = new RuntimeSubsystemStatusService(
            registry,
            externalHealth: health,
            accMode: new StubAccMode(AccServiceMode.Local),
            accHealth: null,
            accIngest: tracker,
            connectors: []);

        var ingest = Assert.Single(service.Current, s => s.Key == "acc-ingest");
        Assert.Equal(SubsystemRuntimeState.Running, ingest.State);
        Assert.Equal(2, ingest.ActiveWorkCount);
    }

    [Fact]
    public void Acc_ingest_is_Idle_when_no_active_background_work()
    {
        var registry = new StartupTaskRegistry();
        var tracker = new StubAccIngestTracker(activeCount: 0);

        using var service = new RuntimeSubsystemStatusService(
            registry,
            accIngest: tracker);

        var ingest = Assert.Single(service.Current, s => s.Key == "acc-ingest");
        Assert.Equal(SubsystemRuntimeState.Idle, ingest.State);
        Assert.Equal(0, ingest.ActiveWorkCount);
    }

    [Fact]
    public void Startup_task_appears_as_Running_then_Idle_after_complete()
    {
        var registry = new StartupTaskRegistry();
        using var service = new RuntimeSubsystemStatusService(registry);

        registry.Begin("pdf-renderer", "מנוע PDF");
        var running = Assert.Single(service.Current, s => s.Key == "pdf-renderer");
        Assert.Equal(SubsystemRuntimeState.Running, running.State);

        registry.Complete("pdf-renderer", succeeded: true, "מוכן");
        var idle = Assert.Single(service.Current, s => s.Key == "pdf-renderer");
        Assert.Equal(SubsystemRuntimeState.Idle, idle.State);
    }

    private sealed class StubAccIngestTracker(int activeCount) : IEmailAccBackgroundWorkTracker
    {
        public int ActiveCount { get; } = activeCount;
        public event Action<int>? ActiveCountChanged;
        public IDisposable BeginWork() => new Noop();
        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubExternalHealth(IReadOnlyList<ExternalHealthCheckSnapshot> current) : IExternalHealthCheckSource
    {
        public IReadOnlyList<ExternalHealthCheckSnapshot> Current { get; } = current;
        public event EventHandler? Changed;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubAccMode(AccServiceMode mode) : IAccServiceModeProvider
    {
        public AccServiceMode Mode { get; } = mode;
        public string? BaseUrl => null;
    }
}
