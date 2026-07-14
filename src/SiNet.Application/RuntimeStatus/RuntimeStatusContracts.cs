namespace SiNet.Application.RuntimeStatus;

/// <summary>Unified runtime state for New System «מצב מערכת» rows.</summary>
public enum SubsystemRuntimeState
{
    Running = 0,
    Idle = 1,
    Degraded = 2,
    Stopped = 3,
    NotConfigured = 4,
}

/// <summary>One subsystem row in the New System status panel.</summary>
public sealed record SubsystemRuntimeStatus(
    string Key,
    string DisplayNameHe,
    SubsystemRuntimeState State,
    int? ActiveWorkCount,
    string SummaryHe,
    DateTimeOffset? LastCheckedUtc);

/// <summary>
/// Aggregates subsystem runtime status for the New System shell and status window.
/// </summary>
public interface IRuntimeSubsystemStatusService
{
    IReadOnlyList<SubsystemRuntimeStatus> Current { get; }

    event EventHandler? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Optional source that contributes one or more status rows to the aggregator.</summary>
public interface ISubsystemStatusContributor
{
    Task<IReadOnlyList<SubsystemRuntimeStatus>> ContributeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Tracks ad-hoc New System startup background tasks (PDF init, Gmail restore, …).</summary>
public interface IStartupTaskRegistry
{
    IReadOnlyList<StartupTaskSnapshot> Tasks { get; }

    event EventHandler? Changed;

    void Begin(string key, string displayNameHe);

    void Complete(string key, bool succeeded, string? summaryHe = null);
}

public sealed record StartupTaskSnapshot(
    string Key,
    string DisplayNameHe,
    StartupTaskPhase Phase,
    string SummaryHe,
    DateTimeOffset UpdatedUtc);

public enum StartupTaskPhase
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
