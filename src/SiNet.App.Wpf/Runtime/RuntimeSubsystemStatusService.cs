using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Common;
using SiNet.Application.Email.Acc;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Runtime;

/// <summary>
/// Aggregates external health, ACC, Gmail, ACC-ingest background work, and startup tasks.
/// </summary>
public sealed class RuntimeSubsystemStatusService : IRuntimeSubsystemStatusService, IDisposable
{
    private readonly IExternalHealthCheckSource? _externalHealth;
    private readonly IAccServiceModeProvider? _accMode;
    private readonly IAccServiceHealthProbe? _accHealth;
    private readonly IEmailAccBackgroundWorkTracker? _accIngest;
    private readonly IEnumerable<IConnectorAuthService> _connectors;
    private readonly IStartupTaskRegistry _startupTasks;
    private readonly object _gate = new();
    private IReadOnlyList<SubsystemRuntimeStatus> _current = [];
    private AccServiceHealthResult? _cachedAccHealth;

    public RuntimeSubsystemStatusService(
        IStartupTaskRegistry startupTasks,
        IExternalHealthCheckSource? externalHealth = null,
        IAccServiceModeProvider? accMode = null,
        IAccServiceHealthProbe? accHealth = null,
        IEmailAccBackgroundWorkTracker? accIngest = null,
        IEnumerable<IConnectorAuthService>? connectors = null)
    {
        _startupTasks = startupTasks ?? throw new ArgumentNullException(nameof(startupTasks));
        _externalHealth = externalHealth;
        _accMode = accMode;
        _accHealth = accHealth;
        _accIngest = accIngest;
        _connectors = connectors ?? [];

        _startupTasks.Changed += OnDependencyChanged;
        if (_externalHealth is not null)
            _externalHealth.Changed += OnDependencyChanged;
        if (_accIngest is not null)
            _accIngest.ActiveCountChanged += OnAccIngestCountChanged;
        foreach (var c in _connectors)
            c.AuthStateChanged += OnAuthStateChanged;

        Rebuild();
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
        if (_externalHealth is not null)
            await _externalHealth.RefreshAsync(cancellationToken).ConfigureAwait(false);

        if (_accHealth is not null && _accMode?.Mode == AccServiceMode.Remote)
        {
            try
            {
                _cachedAccHealth = await _accHealth.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _cachedAccHealth = new AccServiceHealthResult(
                    IsConfigured: true,
                    AccServiceHealthState.Offline,
                    _accMode.BaseUrl,
                    ex.Message);
            }
        }
        else
        {
            _cachedAccHealth = null;
        }

        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _startupTasks.Changed -= OnDependencyChanged;
        if (_externalHealth is not null)
            _externalHealth.Changed -= OnDependencyChanged;
        if (_accIngest is not null)
            _accIngest.ActiveCountChanged -= OnAccIngestCountChanged;
        foreach (var c in _connectors)
            c.AuthStateChanged -= OnAuthStateChanged;
    }

    private void OnDependencyChanged(object? sender, EventArgs e)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAccIngestCountChanged(int _)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAuthStateChanged(bool _)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var rows = new List<SubsystemRuntimeStatus>();
        var now = DateTimeOffset.UtcNow;

        if (_externalHealth is not null)
        {
            foreach (var h in _externalHealth.Current)
            {
                rows.Add(new SubsystemRuntimeStatus(
                    h.Key,
                    h.DisplayNameHe,
                    h.State,
                    ActiveWorkCount: null,
                    h.SummaryHe,
                    h.LastCheckedUtc));
            }
        }

        rows.Add(BuildAccRow(now));
        rows.Add(BuildGmailRow(now));
        rows.Add(BuildAccIngestRow(now));

        foreach (var task in _startupTasks.Current)
        {
            // Avoid duplicating keys already covered by dedicated rows.
            if (string.Equals(task.Key, "gmail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Key, "acc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Key, "acc-ingest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rows.Any(r => string.Equals(r.Key, task.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            rows.Add(new SubsystemRuntimeStatus(
                task.Key,
                task.DisplayNameHe,
                task.State,
                ActiveWorkCount: task.State == SubsystemRuntimeState.Running ? 1 : null,
                task.SummaryHe,
                task.LastChangedUtc));
        }

        lock (_gate)
            _current = rows.OrderBy(r => r.DisplayNameHe, StringComparer.Ordinal).ToList();
    }

    private SubsystemRuntimeStatus BuildAccRow(DateTimeOffset now)
    {
        if (_accMode is null)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.NotConfigured, null,
                "ספק מצב ACC לא רשום", now);
        }

        if (_accMode.Mode == AccServiceMode.Local)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.Idle, null,
                "מצב מקומי (Local)", now);
        }

        var probe = _cachedAccHealth;
        if (probe is null)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.Idle, null,
                $"Remote — {_accMode.BaseUrl} (טרם נבדק — רענן)", now);
        }

        var state = probe.State switch
        {
            AccServiceHealthState.Online => SubsystemRuntimeState.Idle,
            AccServiceHealthState.NotConfigured => SubsystemRuntimeState.NotConfigured,
            _ => SubsystemRuntimeState.Degraded,
        };
        var summary = probe.IsConfigured
            ? $"Remote — {probe.State}" + (string.IsNullOrWhiteSpace(probe.Detail) ? string.Empty : $": {probe.Detail}")
            : "Remote — לא מוגדר";

        return new SubsystemRuntimeStatus("acc", "Autodesk ACC", state, null, summary, now);
    }

    private SubsystemRuntimeStatus BuildGmailRow(DateTimeOffset now)
    {
        var services = _connectors.ToList();
        if (services.Count == 0)
        {
            return new SubsystemRuntimeStatus(
                "gmail", "Gmail / Google", SubsystemRuntimeState.NotConfigured, null,
                "אין שירות אימות מחובר", now);
        }

        var anyAuth = services.Any(s => s.IsAuthenticated);
        if (!anyAuth)
        {
            return new SubsystemRuntimeStatus(
                "gmail", "Gmail / Google", SubsystemRuntimeState.Stopped, null,
                "לא מחובר", now);
        }

        var email = services.Select(s => s.ConnectedAccountEmail).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        return new SubsystemRuntimeStatus(
            "gmail", "Gmail / Google", SubsystemRuntimeState.Idle, null,
            string.IsNullOrWhiteSpace(email) ? "מחובר" : $"מחובר — {email}", now);
    }

    private SubsystemRuntimeStatus BuildAccIngestRow(DateTimeOffset now)
    {
        if (_accIngest is null)
        {
            return new SubsystemRuntimeStatus(
                "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.NotConfigured, null,
                "מעקב עבודת רקע לא רשום", now);
        }

        var count = _accIngest.ActiveCount;
        if (count > 0)
        {
            return new SubsystemRuntimeStatus(
                "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.Running, count,
                $"תהליכי רקע פעילים: {count}", now);
        }

        return new SubsystemRuntimeStatus(
            "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.Idle, 0,
            "אין עבודת רקע פעילה", now);
    }
}
