using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Projects.Dashboard;

/// <summary>
/// Display wrapper for a <see cref="ProjectDashboardRowDto"/> row in the overview grid.
/// </summary>
public sealed class ProjectsDashboardRowVm
{
    public ProjectsDashboardRowVm(ProjectDashboardRowDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Dto = dto;
    }

    public ProjectDashboardRowDto Dto { get; }

    public int ProjectId => Dto.ProjectId;
    public string ProjectNumber => Dto.ProjectNumber;
    public string ProjectName => Dto.ProjectName;
    public string? PlaceName => Dto.PlaceName;
    public string? CompanyName => Dto.CompanyName;
    public string JobTypesDisplay => Dto.JobTypesDisplay;
    public string? Status => Dto.Status;
    public int? StatusId => Dto.StatusId;
    public IReadOnlyList<int> JobTypeIds => Dto.JobTypeIds;
    public string? AssignedUserName => Dto.AssignedUserName;
    public bool IsActive => Dto.IsActive;
    public string ActiveLabel => Dto.IsActive ? "כן" : "לא";
    public DateTime? Start => Dto.Start;
    public DateTime? End => Dto.End;
    public DateTime? Created => Dto.Created;
    public int OpenWorkflowCount => Dto.OpenWorkflowCount;
    public string? OpenWorkflowSummary => Dto.OpenWorkflowSummary;
    public string OpenWorkflowsDisplay =>
        Dto.OpenWorkflowCount == 0
            ? "—"
            : Dto.OpenWorkflowCount == 1
                ? (Dto.OpenWorkflowSummary ?? "1")
                : $"{Dto.OpenWorkflowCount}: {Dto.OpenWorkflowSummary}";
    public int OpenTaskCount => Dto.OpenTaskCount;

    public ProjectSummaryDto ToSummaryDto() => Dto.ToSummaryDto();
}
