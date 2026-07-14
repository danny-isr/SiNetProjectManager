namespace SiNet.Application.RuntimeStatus;

/// <summary>
/// Aggregates <see cref="ISubsystemStatusContributor"/> rows into a stable snapshot for the shell.
/// </summary>
public sealed class RuntimeSubsystemStatusService : IRuntimeSubsystemStatusService, IDisposable
{
    private readonly IReadOnlyList<ISubsystemStatusContributor> _contributors;
    private readonly IStartupTaskRegistry? _startupTasks;
    private readonly object _gate = new();
    private IReadOnlyList<SubsystemRuntimeStatus> _current = [];

    public RuntimeSubsystemStatusService(
        IEnumerable<ISubsystemStatusContributor> contributors,
        IStartupTaskRegistry? startupTasks = null)
    {
        _contributors = (contributors ?? []).ToList();
        _startupTasks = startupTasks;
        if (_startupTasks is not null)
            _startupTasks.Changed += OnUpstreamChanged;
    }

    public IReadOnlyList<SubsystemRuntimeStatus> Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<SubsystemRuntimeStatus>();
        foreach (var contributor in _contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var contributed = await contributor.ContributeAsync(cancellationToken).ConfigureAwait(false);
                if (contributed.Count > 0)
                    rows.AddRange(contributed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                rows.Add(new SubsystemRuntimeStatus(
                    Key: $"error:{contributor.GetType().Name}",
                    DisplayNameHe: contributor.GetType().Name,
                    State: SubsystemRuntimeState.Degraded,
                    ActiveWorkCount: null,
                    SummaryHe: $"שגיאה בקריאת סטטוס: {ex.Message}",
                    LastCheckedUtc: DateTimeOffset.UtcNow));
            }
        }

        // De-dupe by key (later contributors win).
        var byKey = new Dictionary<string, SubsystemRuntimeStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            byKey[row.Key] = row;

        var snapshot = byKey.Values
            .OrderBy(r => r.DisplayNameHe, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_gate)
            _current = snapshot;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_startupTasks is not null)
            _startupTasks.Changed -= OnUpstreamChanged;
    }

    private void OnUpstreamChanged(object? sender, EventArgs e) =>
        _ = RefreshAsync(CancellationToken.None);
}
