using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.Projects;

/// <summary>
/// Read-only SQL implementation of <see cref="IProjectDashboardQueryService"/>
/// (see <c>docs/PROJECTS_DASHBOARD.md</c>). Aggregates open workflows and open tasks without
/// loading full workflow stage graphs.
/// </summary>
public sealed class ProjectDashboardQueryService : IProjectDashboardQueryService
{
    private static readonly WorkflowStatus[] OpenWorkflowStatuses =
    [
        WorkflowStatus.Active,
        WorkflowStatus.Paused,
    ];

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public ProjectDashboardQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectDashboardRowDto>> GetRowsAsync(
        ProjectDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var projectsQuery = db.Projects
            .AsNoTracking()
            .Where(p => p.NameAndNumber != null);

        if (!query.IncludeClosed)
        {
            projectsQuery = projectsQuery.Where(p => p.EndOfProject != true);
        }

        var projectRows = await projectsQuery
            .Select(p => new ProjectDashRow
            {
                Id = p.Id,
                Number = p.Number,
                Title = p.Title,
                NameAndNumber = p.NameAndNumber,
                PlaceName = p.Place != null ? p.Place.Title : null,
                CompanyName = p.Company != null ? p.Company.Title : null,
                StatusId = p.ProjectStatusId,
                StatusName = p.ProjectStatus != null ? p.ProjectStatus.Title : null,
                StatusCode = p.ProjectStatus != null ? p.ProjectStatus.Code : null,
                JobTypeNames = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectType != null && t.ProjectType.Title != null)
                    .Select(t => t.ProjectType!.Title!)
                    .ToList(),
                JobTypeIds = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectTypeId != null)
                    .Select(t => t.ProjectTypeId!.Value)
                    .ToList(),
                AssignedUserName = p.Worker,
                IsActive = p.EndOfProject != true,
                Start = p.Start,
                End = p.End,
                Created = p.Created,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (projectRows.Count == 0)
        {
            return [];
        }

        var projectIds = projectRows.Select(p => p.Id).ToList();

        var openWorkflows = await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => projectIds.Contains(i.ProjectId)
                        && i.IsProjectBound
                        && OpenWorkflowStatuses.Contains(i.Status))
            .Select(i => new OpenWorkflowRow
            {
                ProjectId = i.ProjectId,
                DefinitionName = i.WorkflowDefinition.Name,
                StageName = i.CurrentStage != null ? i.CurrentStage.Name : null,
                CreatedAtUtc = i.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var workflowsByProject = openWorkflows
            .GroupBy(w => w.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(w => w.CreatedAtUtc).ToList());

        var openTaskCounts = await db.ProjectAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId != null
                        && projectIds.Contains(a.ProjectId.Value)
                        && a.AssignmentStatus != null
                        && a.AssignmentStatus.IsOpen)
            .GroupBy(a => a.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskCountByProject = openTaskCounts.ToDictionary(x => x.ProjectId, x => x.Count);

        return projectRows
            .OrderByDescending(p => p.Number ?? float.MinValue)
            .Select(p =>
            {
                workflowsByProject.TryGetValue(p.Id, out var workflows);
                workflows ??= [];
                taskCountByProject.TryGetValue(p.Id, out var taskCount);

                return new ProjectDashboardRowDto(
                    ProjectId: p.Id,
                    ProjectNumber: FormatNumber(p.Number),
                    ProjectNumberValue: p.Number,
                    ProjectName: p.Title ?? string.Empty,
                    PlaceName: NullIfBlank(p.PlaceName),
                    CompanyName: NullIfBlank(p.CompanyName),
                    JobTypeNames: p.JobTypeNames,
                    JobTypeIds: p.JobTypeIds,
                    Status: NullIfBlank(p.StatusName),
                    StatusCode: NullIfBlank(p.StatusCode),
                    StatusId: p.StatusId,
                    AssignedUserName: NullIfBlank(p.AssignedUserName),
                    IsActive: p.IsActive,
                    Start: p.Start,
                    End: p.End,
                    Created: p.Created,
                    OpenWorkflowCount: workflows.Count,
                    OpenWorkflowSummary: BuildWorkflowSummary(workflows),
                    OpenTaskCount: taskCount,
                    ProjectLabelName: NullIfBlank(p.NameAndNumber));
            })
            .ToList();
    }

    private static string? BuildWorkflowSummary(IReadOnlyList<OpenWorkflowRow> workflows)
    {
        if (workflows.Count == 0)
        {
            return null;
        }

        static string FormatOne(OpenWorkflowRow w)
        {
            var name = string.IsNullOrWhiteSpace(w.DefinitionName) ? "?" : w.DefinitionName;
            return string.IsNullOrWhiteSpace(w.StageName)
                ? name
                : $"{name} — {w.StageName}";
        }

        if (workflows.Count == 1)
        {
            return FormatOne(workflows[0]);
        }

        return string.Join("; ", workflows.Select(FormatOne));
    }

    private static string FormatNumber(float? number)
    {
        if (number is not float value)
        {
            return string.Empty;
        }

        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001f
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class ProjectDashRow
    {
        public int Id { get; init; }
        public float? Number { get; init; }
        public string? Title { get; init; }
        public string? NameAndNumber { get; init; }
        public string? PlaceName { get; init; }
        public string? CompanyName { get; init; }
        public int? StatusId { get; init; }
        public string? StatusName { get; init; }
        public string? StatusCode { get; init; }
        public List<string> JobTypeNames { get; init; } = [];
        public List<int> JobTypeIds { get; init; } = [];
        public string? AssignedUserName { get; init; }
        public bool IsActive { get; init; }
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
        public DateTime? Created { get; init; }
    }

    private sealed class OpenWorkflowRow
    {
        public int ProjectId { get; init; }
        public string DefinitionName { get; init; } = string.Empty;
        public string? StageName { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
