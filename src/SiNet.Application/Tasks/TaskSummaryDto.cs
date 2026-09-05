namespace SiNet.Application.Tasks;

/// <summary>
/// Read-only task row for lists and shell panels. No EF entities cross this boundary.
/// </summary>
public sealed record TaskSummaryDto(
    int TaskId,
    int? ProjectId,
    string? TaskTypeCode,
    string? TaskTypeName,
    string? StatusCode,
    string? StatusName,
    bool IsOpen,
    int? AssignedToUserId,
    string? AssignedToUserName,
    int WorkQueueBucket,
    string WorkQueueBucketCode,
    string WorkQueueBucketDisplayName,
    int? WorkPriority,
    DateTime? DueDate,
    /// <summary>When the task row was created/opened (<c>ProjectAssignment.Created</c>), UTC when stored as UTC.</summary>
    DateTime? CreatedAt,
    string? LastTaskResultCode,
    string? Title,
    string? ComponentKey,
    string? WorkflowDefinitionName = null,
    string? JobTypeTitle = null,
    string? CurrentStageName = null,
    /// <summary>Preformatted process · track · stage line for list cards (null when unknown).</summary>
    string? TrackDisplayLine = null,
    /// <summary>Business project number display (same formatting as project selector).</summary>
    string? ProjectNumber = null,
    /// <summary>Project title / name for operator display.</summary>
    string? ProjectName = null)
{
    /// <summary>Compact visible id, e.g. <c>#293</c>.</summary>
    public string TaskIdDisplay => $"#{TaskId}";

    /// <summary>Hebrew tooltip for the visible task number.</summary>
    public string TaskIdTooltip => $"מספר משימה {TaskId}";

    /// <summary>
    /// Operator project line: <c>פרויקט {number} — {name}</c>, or null when no project.
    /// Never uses internal <see cref="ProjectId"/> as the primary text.
    /// </summary>
    public string? ProjectDisplayLine
    {
        get
        {
            var number = ProjectNumber?.Trim();
            var name = ProjectName?.Trim();
            if (string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(number))
            {
                return $"פרויקט {name}";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return $"פרויקט {number}";
            }

            return $"פרויקט {number} — {name}";
        }
    }

    /// <summary>Diagnostics-only tooltip; may include internal ProjectId.</summary>
    public string? ProjectDisplayTooltip
    {
        get
        {
            var line = ProjectDisplayLine;
            if (line is null)
            {
                return ProjectId is int id ? $"ProjectId={id}" : null;
            }

            return ProjectId is int projectId ? $"{line} (ProjectId={projectId})" : line;
        }
    }

    /// <summary>True when TaskTypeName adds information beyond Title.</summary>
    public bool ShowTaskTypeName =>
        !string.IsNullOrWhiteSpace(TaskTypeName)
        && !string.Equals(TaskTypeName.Trim(), Title?.Trim(), StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? $"Task {TaskId}" : Title;
}
