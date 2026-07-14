namespace SiNet.Application.RuntimeStatus;

/// <summary>In-memory registry of New System startup background tasks.</summary>
public sealed class StartupTaskRegistry : IStartupTaskRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StartupTaskSnapshot> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StartupTaskSnapshot> Tasks
    {
        get
        {
            lock (_gate)
            {
                return _tasks.Values.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    public event EventHandler? Changed;

    public void Begin(string key, string displayNameHe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            _tasks[key] = new StartupTaskSnapshot(
                key,
                string.IsNullOrWhiteSpace(displayNameHe) ? key : displayNameHe,
                StartupTaskPhase.Running,
                "רץ ברקע…",
                DateTimeOffset.UtcNow);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(string key, bool succeeded, string? summaryHe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (!_tasks.TryGetValue(key, out var existing))
            {
                existing = new StartupTaskSnapshot(
                    key,
                    key,
                    StartupTaskPhase.Pending,
                    string.Empty,
                    DateTimeOffset.UtcNow);
            }

            _tasks[key] = existing with
            {
                Phase = succeeded ? StartupTaskPhase.Succeeded : StartupTaskPhase.Failed,
                SummaryHe = summaryHe
                    ?? (succeeded ? "הושלם" : "נכשל"),
                UpdatedUtc = DateTimeOffset.UtcNow,
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
