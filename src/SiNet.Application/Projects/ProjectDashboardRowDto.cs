namespace SiNet.Application.Projects;

/// <summary>
/// Read-only row for the Projects Overview Dashboard
/// (see <c>docs/PROJECTS_DASHBOARD.md</c>). Distinct from <see cref="ProjectSummaryDto"/> so the
/// shared Project Selector contract stays unchanged.
/// </summary>
/// <param name="ProjectId">Project identifier.</param>
/// <param name="ProjectNumber">Formatted project number for display/search.</param>
/// <param name="ProjectName">Project title.</param>
/// <param name="PlaceName">Place / city; <see langword="null"/> when unknown.</param>
/// <param name="CompanyName">Company / client; <see langword="null"/> when unknown.</param>
/// <param name="JobTypeNames">All linked job-type titles (may be empty).</param>
/// <param name="JobTypeIds">All linked job-type ids (may be empty).</param>
/// <param name="Status">Business lifecycle status title (<c>ProjectStatus</c>).</param>
/// <param name="StatusCode">Stable status code; <see langword="null"/> when unknown.</param>
/// <param name="StatusId">Status id for filtering; <see langword="null"/> when unknown.</param>
/// <param name="AssignedUserName">Responsible worker display name; <see langword="null"/> when unset.</param>
/// <param name="IsActive"><see langword="true"/> when not ended (<c>EndOfProject != true</c>).</param>
/// <param name="Start">Project start date; <see langword="null"/> when unset.</param>
/// <param name="End">Project end date; <see langword="null"/> when unset.</param>
/// <param name="Created">Project created timestamp; <see langword="null"/> when unset.</param>
/// <param name="OpenWorkflowCount">Count of Active/Paused project-bound workflow instances.</param>
/// <param name="OpenWorkflowSummary">Human-readable open-workflow summary (name + stage).</param>
/// <param name="OpenTaskCount">Count of open project assignments.</param>
/// <param name="ProjectLabelName">Canonical label leaf (<c>NameAndNumber</c>); for Current Project mapping.</param>
public sealed record ProjectDashboardRowDto(
    int ProjectId,
    string ProjectNumber,
    string ProjectName,
    string? PlaceName,
    string? CompanyName,
    IReadOnlyList<string> JobTypeNames,
    IReadOnlyList<int> JobTypeIds,
    string? Status,
    string? StatusCode,
    int? StatusId,
    string? AssignedUserName,
    bool IsActive,
    DateTime? Start,
    DateTime? End,
    DateTime? Created,
    int OpenWorkflowCount,
    string? OpenWorkflowSummary,
    int OpenTaskCount,
    string? ProjectLabelName = null)
{
    /// <summary>Joined job-type titles for grid display.</summary>
    public string JobTypesDisplay =>
        JobTypeNames.Count == 0 ? string.Empty : string.Join(", ", JobTypeNames);

    /// <summary>
    /// Projects a selector-compatible summary for Current Project / drill-down without leaking
    /// dashboard-only fields into the selector contract.
    /// </summary>
    public ProjectSummaryDto ToSummaryDto() => new(
        ProjectId: ProjectId,
        ProjectNumber: ProjectNumber,
        ProjectName: ProjectName,
        PlaceName: PlaceName,
        CompanyName: CompanyName,
        JobType: JobTypeNames.Count > 0 ? JobTypeNames[0] : null,
        Status: Status,
        AssignedUserName: AssignedUserName,
        IsActive: IsActive,
        StatusId: StatusId,
        JobTypeIds: JobTypeIds,
        ProjectLabelName: ProjectLabelName);
}
