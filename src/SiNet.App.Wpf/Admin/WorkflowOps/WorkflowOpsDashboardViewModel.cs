using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Runtime;
using SiNet.Application.Workflow;
using SiNet.Domain.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

/// <summary>
/// Read-only Workflow Ops Dashboard VM — see docs/WORKFLOW_OPS_DASHBOARD.md.
/// </summary>
public sealed class WorkflowOpsDashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(20);

    private readonly IWorkflowQueryService _query;
    private readonly IWorkflowRecoveryService? _recovery;
    private readonly IRuntimeSubsystemStatusService? _runtime;
    private readonly IServiceProvider _services;
    private readonly EventHandler _timerTick;
    private DispatcherTimer? _timer;

    private bool _isBusy;
    private bool _disposed;
    private string _overallStatusText = "טוען…";
    private string _overallStatusTone = "Unknown";
    private string _activeCountText = "—";
    private string _completedTodayText = "—";
    private string _cancelledTodayText = "—";
    private string _stalledCountText = "—";
    private string _avgDurationText = "—";
    private string _lastRefreshText = "—";
    private string _infraSummaryText = "—";
    private string _filterText = string.Empty;
    private string? _statusFilter;
    private string? _workflowNameFilter;
    private string _detailSummary = "בחר מופע מהטבלה לפירוט שלבים.";
    private string _detailTransitions = string.Empty;
    private string _dangerousActionsHint = "Retry / ביטול — בקרוב (לא ב-MVP).";
    private WorkflowOpsInstanceRowVm? _selected;
    private IReadOnlyList<WorkflowOpsInstanceRowVm> _allRows = [];

    public WorkflowOpsDashboardViewModel(
        IWorkflowQueryService query,
        IServiceProvider services,
        IWorkflowRecoveryService? recovery = null,
        IRuntimeSubsystemStatusService? runtime = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _recovery = recovery;
        _runtime = runtime;

        Rows = new ObservableCollection<WorkflowOpsInstanceRowVm>();
        StatusFilterOptions = new ObservableCollection<string>
        {
            "(הכל)",
            "פעיל",
            "מושהה",
            "הושלם",
            "בוטל",
            "טיוטה",
            "חשוד כתקוע",
        };
        WorkflowNameFilterOptions = new ObservableCollection<string> { "(הכל)" };
        StatusFilter = "(הכל)";
        WorkflowNameFilter = "(הכל)";

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenSystemStatusCommand = new RelayCommand(_ => OpenSystemStatus(), _ => true);
        CopyInstanceIdCommand = new RelayCommand(
            _ => CopySelectedInstanceId(),
            _ => Selected is not null);
        RetryCommand = new RelayCommand(_ => { }, _ => false);
        CancelWorkflowCommand = new RelayCommand(_ => { }, _ => false);

        // Created in LoadAsync so unit tests can call RefreshAsync without a WPF Dispatcher.
        _timerTick = async (_, _) =>
        {
            if (!IsBusy && !_disposed)
                await RefreshAsync().ConfigureAwait(true);
        };
    }

    public ObservableCollection<WorkflowOpsInstanceRowVm> Rows { get; }
    public ObservableCollection<string> StatusFilterOptions { get; }
    public ObservableCollection<string> WorkflowNameFilterOptions { get; }

    public string OverallStatusText
    {
        get => _overallStatusText;
        private set => SetField(ref _overallStatusText, value);
    }

    public string OverallStatusTone
    {
        get => _overallStatusTone;
        private set => SetField(ref _overallStatusTone, value);
    }

    public string ActiveCountText
    {
        get => _activeCountText;
        private set => SetField(ref _activeCountText, value);
    }

    public string CompletedTodayText
    {
        get => _completedTodayText;
        private set => SetField(ref _completedTodayText, value);
    }

    public string CancelledTodayText
    {
        get => _cancelledTodayText;
        private set => SetField(ref _cancelledTodayText, value);
    }

    public string StalledCountText
    {
        get => _stalledCountText;
        private set => SetField(ref _stalledCountText, value);
    }

    public string AvgDurationText
    {
        get => _avgDurationText;
        private set => SetField(ref _avgDurationText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetField(ref _lastRefreshText, value);
    }

    public string InfraSummaryText
    {
        get => _infraSummaryText;
        private set => SetField(ref _infraSummaryText, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value))
                return;
            ApplyFilters();
        }
    }

    public string? StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!SetField(ref _statusFilter, value))
                return;
            ApplyFilters();
        }
    }

    public string? WorkflowNameFilter
    {
        get => _workflowNameFilter;
        set
        {
            if (!SetField(ref _workflowNameFilter, value))
                return;
            ApplyFilters();
        }
    }

    public WorkflowOpsInstanceRowVm? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value))
                return;
            (CopyInstanceIdCommand as RelayCommand)?.RaiseCanExecuteChanged();
            _ = LoadDetailAsync(value);
        }
    }

    public string DetailSummary
    {
        get => _detailSummary;
        private set => SetField(ref _detailSummary, value);
    }

    public string DetailTransitions
    {
        get => _detailTransitions;
        private set => SetField(ref _detailTransitions, value);
    }

    public string DangerousActionsHint
    {
        get => _dangerousActionsHint;
        private set => SetField(ref _dangerousActionsHint, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenSystemStatusCommand { get; }
    public ICommand CopyInstanceIdCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CancelWorkflowCommand { get; }

    public async Task LoadAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        if (_disposed)
            return;

        _timer ??= new DispatcherTimer { Interval = AutoRefreshInterval };
        _timer.Tick -= _timerTick;
        _timer.Tick += _timerTick;
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_timer is null)
            return;
        _timer.Stop();
        _timer.Tick -= _timerTick;
        _timer = null;
    }

    internal async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var snapshots = await _query.GetAllWorkflowInstanceSnapshotsAsync(CancellationToken.None)
                .ConfigureAwait(true);
            IReadOnlyList<WorkflowRecoveryCandidate> stalled = [];
            if (_recovery is not null)
            {
                try
                {
                    stalled = await _recovery.DetectStalledAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning($"[WorkflowOps] DetectStalled failed: {ex.Message}");
                }
            }

            var stalledIds = stalled.Select(s => s.InstanceId).ToHashSet();
            var utcNow = DateTime.UtcNow;
            var rows = snapshots
                .Select(s => new WorkflowOpsInstanceRowVm(s, stalledIds.Contains(s.Instance.Id), utcNow))
                .OrderByDescending(r => r.IsStalled)
                .ThenByDescending(r => r.StartedLocal)
                .ToList();

            _allRows = rows;
            RebuildWorkflowNameFilters(rows);
            ApplySummary(rows, stalledIds.Count);
            ApplyInfraSummary();
            ApplyFilters(preserveSelectionId: Selected?.InstanceId);
            LastRefreshText = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            OverallStatusText = $"שגיאה בטעינה: {ex.Message}";
            OverallStatusTone = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildWorkflowNameFilters(IReadOnlyList<WorkflowOpsInstanceRowVm> rows)
    {
        var names = rows.Select(r => r.WorkflowName).Distinct(StringComparer.Ordinal).OrderBy(n => n).ToList();
        var previous = WorkflowNameFilter;
        WorkflowNameFilterOptions.Clear();
        WorkflowNameFilterOptions.Add("(הכל)");
        foreach (var name in names)
            WorkflowNameFilterOptions.Add(name);
        if (previous is not null && WorkflowNameFilterOptions.Contains(previous))
            _workflowNameFilter = previous;
        else
            _workflowNameFilter = "(הכל)";
        OnPropertyChanged(nameof(WorkflowNameFilter));
    }

    private void ApplySummary(IReadOnlyList<WorkflowOpsInstanceRowVm> rows, int stalledCount)
    {
        var today = DateTime.Today;
        var active = rows.Count(r => r.Status is WorkflowStatus.Active or WorkflowStatus.Paused);
        var completedToday = rows.Count(r =>
            r.Status == WorkflowStatus.Completed
            && r.Snapshot.Instance.CompletedAtUtc is { } c
            && ToLocalDate(c) == today);
        var cancelledToday = rows.Count(r =>
            r.Status == WorkflowStatus.Cancelled
            && r.Snapshot.Instance.CompletedAtUtc is { } c
            && ToLocalDate(c) == today);

        var completedDurations = rows
            .Where(r => r.Status == WorkflowStatus.Completed && r.Snapshot.Instance.CompletedAtUtc is not null)
            .Where(r => ToLocalDate(r.Snapshot.Instance.CompletedAtUtc!.Value) == today)
            .Select(r => r.Snapshot.Instance.CompletedAtUtc!.Value - r.Snapshot.Instance.CreatedAtUtc)
            .Where(ts => ts >= TimeSpan.Zero)
            .ToList();

        ActiveCountText = active.ToString();
        CompletedTodayText = completedToday.ToString();
        CancelledTodayText = cancelledToday.ToString();
        StalledCountText = stalledCount.ToString();
        AvgDurationText = completedDurations.Count == 0
            ? "—"
            : FormatAvg(TimeSpan.FromTicks((long)completedDurations.Average(t => t.Ticks)));

        if (stalledCount > 0)
        {
            OverallStatusText = stalledCount == 1
                ? "אזהרה — תהליך אחד חשוד כתקוע"
                : $"אזהרה — {stalledCount} תהליכים חשודים כתקועים";
            OverallStatusTone = "Warning";
        }
        else if (active > 0)
        {
            OverallStatusText = "תקין — יש תהליכים פעילים";
            OverallStatusTone = "Ok";
        }
        else
        {
            OverallStatusText = "תקין — אין תהליכים פעילים";
            OverallStatusTone = "Ok";
        }
    }

    private void ApplyInfraSummary()
    {
        if (_runtime is null)
        {
            InfraSummaryText = "מצב תשתיות לא זמין ב-DI";
            return;
        }

        var statuses = _runtime.Current;
        var ok = statuses.Count(s =>
            s.State is SubsystemRuntimeState.Idle or SubsystemRuntimeState.Running);
        var bad = statuses.Count(s =>
            s.State is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.Stopped);
        InfraSummaryText = $"{ok} תקינים · {bad} בתקלה · סה״כ {statuses.Count} מערכות־משנה";
        if (bad > 0 && OverallStatusTone == "Ok")
        {
            OverallStatusText = "אזהרה — תקלה בתשתית (ראה מצב מערכת)";
            OverallStatusTone = "Warning";
        }
    }

    private void ApplyFilters(int? preserveSelectionId = null)
    {
        IEnumerable<WorkflowOpsInstanceRowVm> q = _allRows;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var term = FilterText.Trim();
            q = q.Where(r =>
                r.ProjectDisplay.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.UserDisplay.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.InstanceId.ToString().Contains(term, StringComparison.Ordinal)
                || r.Notes.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(StatusFilter) && StatusFilter != "(הכל)")
        {
            q = StatusFilter switch
            {
                "פעיל" => q.Where(r => r.Status == WorkflowStatus.Active),
                "מושהה" => q.Where(r => r.Status == WorkflowStatus.Paused),
                "הושלם" => q.Where(r => r.Status == WorkflowStatus.Completed),
                "בוטל" => q.Where(r => r.Status == WorkflowStatus.Cancelled),
                "טיוטה" => q.Where(r => r.Status == WorkflowStatus.Draft),
                "חשוד כתקוע" => q.Where(r => r.IsStalled),
                _ => q,
            };
        }

        if (!string.IsNullOrWhiteSpace(WorkflowNameFilter) && WorkflowNameFilter != "(הכל)")
            q = q.Where(r => string.Equals(r.WorkflowName, WorkflowNameFilter, StringComparison.Ordinal));

        var list = q.ToList();
        Rows.Clear();
        foreach (var row in list)
            Rows.Add(row);

        if (preserveSelectionId is { } id)
            Selected = Rows.FirstOrDefault(r => r.InstanceId == id);
    }

    private async Task LoadDetailAsync(WorkflowOpsInstanceRowVm? row)
    {
        if (row is null)
        {
            DetailSummary = "בחר מופע מהטבלה לפירוט שלבים.";
            DetailTransitions = string.Empty;
            return;
        }

        var transitions = row.Snapshot.Instance.StageTransitions
            .OrderBy(t => t.TransitionedAtUtc)
            .Select(t =>
            {
                var stage = t.ToStage?.Name ?? $"#{t.ToStageId}";
                var when = t.TransitionedAtUtc.ToLocalTime().ToString("dd/MM HH:mm");
                var by = t.TransitionedByUser?.PersonName ?? "—";
                return $"{when} → {stage} ({by})";
            });
        DetailTransitions = string.Join(Environment.NewLine, transitions);
        DetailSummary =
            $"#{row.InstanceId} · {row.WorkflowName} · {row.ProjectDisplay} · שלב {row.StageName} · {row.StatusLabel}"
            + (row.IsStalled ? " · חשוד כתקוע" : string.Empty);

        try
        {
            var progress = await _query.GetStageTaskProgressAsync(row.InstanceId, CancellationToken.None)
                .ConfigureAwait(true);
            row.ApplyTaskProgress(progress);
            DetailSummary += Environment.NewLine + row.TaskProgressText;
        }
        catch (Exception ex)
        {
            DetailSummary += Environment.NewLine + $"(התקדמות משימות לא זמינה: {ex.Message})";
        }
    }

    private void CopySelectedInstanceId()
    {
        if (Selected is null)
            return;
        try
        {
            Clipboard.SetText(Selected.InstanceId.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"העתקה נכשלה: {ex.Message}",
                "בריאות תהליכים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSystemStatus()
    {
        try
        {
            ThemeResourceLoader.EnsureApplicationResourcesMerged();
            var window = _services.GetRequiredService<SystemStatusWindow>();
            if (System.Windows.Application.Current?.MainWindow is { } owner
                && !ReferenceEquals(owner, window))
            {
                window.Owner = owner;
            }

            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"שגיאה בפתיחת מצב מערכת: {ex.Message}",
                "בריאות תהליכים",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static DateTime ToLocalDate(DateTime utc) =>
        (utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc).ToLocalTime().Date;

    private static string FormatAvg(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{span.TotalHours:0.#} שע׳";
        return $"{span.TotalMinutes:0} דק׳";
    }
}
