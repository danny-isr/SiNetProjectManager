using SiNet.App.Wpf.Inspection;
using SiNet.Application.Workflow;
using SiNet.Domain.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class WorkflowOpsInstanceRowVm : ObservableObject
{
    private bool _isStalled;
    private string _taskProgressText = string.Empty;

    public WorkflowOpsInstanceRowVm(WorkflowInstanceSnapshotDto snapshot, bool isStalled, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        var instance = snapshot.Instance;
        InstanceId = instance.Id;
        WorkflowName = instance.WorkflowDefinition?.Name ?? $"#{instance.WorkflowDefinitionId}";
        ProjectDisplay = FormatProject(instance);
        UserDisplay = string.IsNullOrWhiteSpace(instance.CreatedByUser?.PersonName)
            ? "—"
            : instance.CreatedByUser!.PersonName!;
        StartedLocal = ToLocal(instance.CreatedAtUtc);
        StageName = instance.CurrentStage?.Name ?? "—";
        Status = instance.Status;
        StatusLabel = Status switch
        {
            WorkflowStatus.Draft => "טיוטה",
            WorkflowStatus.Active => "פעיל",
            WorkflowStatus.Paused => "מושהה",
            WorkflowStatus.Completed => "הושלם",
            WorkflowStatus.Cancelled => "בוטל",
            _ => Status.ToString(),
        };
        Notes = Truncate(instance.Notes, 80);
        LastActivityUtc = instance.StageTransitions.Count > 0
            ? instance.StageTransitions.Max(t => t.TransitionedAtUtc)
            : instance.CreatedAtUtc;
        LastActivityLocal = ToLocal(LastActivityUtc);
        DurationText = FormatDuration(instance, utcNow);
        IsStalled = isStalled;
    }

    public WorkflowInstanceSnapshotDto Snapshot { get; }

    public int InstanceId { get; }
    public string WorkflowName { get; }
    public string ProjectDisplay { get; }
    public string UserDisplay { get; }
    public DateTime StartedLocal { get; }
    public string StageName { get; }
    public WorkflowStatus Status { get; }
    public string StatusLabel { get; }
    public string Notes { get; }
    public DateTime LastActivityUtc { get; }
    public DateTime LastActivityLocal { get; }
    public string DurationText { get; }

    public bool IsStalled
    {
        get => _isStalled;
        private set
        {
            if (!SetField(ref _isStalled, value))
                return;
            OnPropertyChanged(nameof(StalledBadge));
        }
    }

    public string StalledBadge => IsStalled ? "חשוד כתקוע" : string.Empty;

    public string TaskProgressText
    {
        get => _taskProgressText;
        set => SetField(ref _taskProgressText, value);
    }

    public void ApplyTaskProgress(StageTaskProgressDto progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        TaskProgressText =
            $"משימות: נדרשות {progress.CompletedRequired}/{progress.TotalRequired} · סגורות {progress.TotalClosed}/{progress.TotalCreated}";
    }

    private static string FormatProject(WorkflowInstanceDto instance)
    {
        if (instance.Project is { } p)
        {
            var num = p.Number?.ToString("0.###") ?? "?";
            var name = string.IsNullOrWhiteSpace(p.Title) ? string.Empty : $" — {p.Title}";
            return $"{num}{name}";
        }

        return instance.ProjectId is { } id ? $"#{id}" : "—";
    }

    private static DateTime ToLocal(DateTime utc) =>
        utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
            : utc.ToLocalTime();

    private static string FormatDuration(WorkflowInstanceDto instance, DateTime utcNow)
    {
        var end = instance.CompletedAtUtc ?? utcNow;
        var span = end - instance.CreatedAtUtc;
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}י {span.Hours}ש";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}ש {span.Minutes}ד";
        return $"{(int)span.TotalMinutes}ד";
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}
