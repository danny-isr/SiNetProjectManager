namespace SiNet.Application.Runtime;

/// <summary>
/// Tracks ad-hoc New System startup / background tasks (PDF renderer, silent Gmail restore, …)
/// so the shell can show Running vs Idle without <c>IHostedService</c>.
/// </summary>
public interface IStartupTaskRegistry
{
    IReadOnlyList<StartupTaskSnapshot> Current { get; }

    event EventHandler? Changed;

    void Begin(string key, string displayNameHe);

    void Complete(string key, bool succeeded, string? summaryHe = null);

    void SetIdle(string key, string? summaryHe = null);
}

/// <summary>Snapshot of one registered startup/background task.</summary>
public sealed record StartupTaskSnapshot(
    string Key,
    string DisplayNameHe,
    SubsystemRuntimeState State,
    string SummaryHe,
    DateTimeOffset? LastChangedUtc);
