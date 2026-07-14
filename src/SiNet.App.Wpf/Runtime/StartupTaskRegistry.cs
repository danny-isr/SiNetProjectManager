using System.Collections.Concurrent;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Runtime;

/// <summary>In-memory startup/background task tracker for New System.</summary>
public sealed class StartupTaskRegistry : IStartupTaskRegistry
{
    private readonly ConcurrentDictionary<string, StartupTaskSnapshot> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StartupTaskSnapshot> Current =>
        _tasks.Values.OrderBy(t => t.DisplayNameHe, StringComparer.Ordinal).ToList();

    public event EventHandler? Changed;

    public void Begin(string key, string displayNameHe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var name = string.IsNullOrWhiteSpace(displayNameHe) ? key : displayNameHe;
        _tasks[key] = new StartupTaskSnapshot(
            key,
            name,
            SubsystemRuntimeState.Running,
            "רץ ברקע…",
            DateTimeOffset.UtcNow);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(string key, bool succeeded, string? summaryHe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_tasks.TryGetValue(key, out var existing))
        {
            existing = new StartupTaskSnapshot(key, key, SubsystemRuntimeState.Idle, string.Empty, null);
        }

        _tasks[key] = existing with
        {
            State = succeeded ? SubsystemRuntimeState.Idle : SubsystemRuntimeState.Degraded,
            SummaryHe = summaryHe
                ?? (succeeded ? "הושלם" : "נכשל"),
            LastChangedUtc = DateTimeOffset.UtcNow,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetIdle(string key, string? summaryHe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_tasks.TryGetValue(key, out var existing))
            return;

        _tasks[key] = existing with
        {
            State = SubsystemRuntimeState.Idle,
            SummaryHe = summaryHe ?? "מוכן",
            LastChangedUtc = DateTimeOffset.UtcNow,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
