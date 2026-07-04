using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;

namespace SiNetSQL.Services.Projects;

/// <summary>
/// Real, <b>read-only</b> <see cref="IProjectQueryService"/> backed by the existing SiNetSQL EF model
/// (see <c>docs/PROJECTS.md</c> §5/§6 and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// It mirrors the legacy project-loading authority
/// (<c>SiNetProjectManagerV2/Dialogs/ProjectSelectorDialog.LoadProjects</c>): projects are read through
/// <see cref="IDbContextFactory{TContext}"/> with <c>AsNoTracking()</c>, filtered server-side to rows
/// that have a <c>NameAndNumber</c>, projected to clean <see cref="ProjectSummaryDto"/> rows at the
/// boundary (EF entities never leak into WPF), then narrowed/ordered by the shared
/// <see cref="ProjectSummaryQuery"/> helper so the selector's parity behavior (dummy-number exclusion,
/// active/include-closed, job-type/status/free-text filters, number-descending order) matches the fake
/// source exactly.
/// </para>
/// <para>
/// This service performs <b>no writes</b> and touches <b>no schema</b>. It only issues read queries; it
/// never adds, updates, or deletes entities and never mutates workflow, tasks, or files.
/// </para>
/// </summary>
public sealed class ProjectQueryService : IProjectQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    /// <summary>
    /// Creates the service over the shared <see cref="SiNetSQLDbContext"/> factory. The factory (and its
    /// connection string) is supplied by the host composition root; this service performs no lookup.
    /// </summary>
    public ProjectQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
        ProjectSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var queryable = db.Projects
            .AsNoTracking()
            .Where(p => p.NameAndNumber != null);

        if (query.StatusId is int statusId)
        {
            queryable = queryable.Where(p => p.ProjectStatusId == statusId);
        }

        if (query.JobTypeId is int jobTypeId)
        {
            queryable = queryable.Where(p =>
                p.TypeOfProjectInProjects.Any(t => t.ProjectTypeId == jobTypeId));
        }

        if (!query.IncludeClosed)
        {
            queryable = queryable.Where(p => p.EndOfProject != true);
        }

        // Server-side: mirror the legacy load (NameAndNumber present) and project only the columns the
        // selector needs. Navigation titles (Place/Company/Status) and linked project types are read
        // via correlated sub-selects so EF entities never materialize past this boundary.
        var rows = await queryable
            .Select(p => new ProjectRow
            {
                Id = p.Id,
                Number = p.Number,
                Title = p.Title,
                NameAndNumber = p.NameAndNumber,
                PlaceName = p.Place != null ? p.Place.Title : null,
                CompanyName = p.Company != null ? p.Company.Title : null,
                StatusId = p.ProjectStatusId,
                StatusName = p.ProjectStatus != null ? p.ProjectStatus.Title : null,
                // Display-only: surface the first type title; filtering uses all linked type ids.
                JobType = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectType != null && t.ProjectType.Title != null)
                    .Select(t => t.ProjectType!.Title)
                    .FirstOrDefault(),
                JobTypeIds = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectTypeId != null)
                    .Select(t => t.ProjectTypeId!.Value)
                    .ToList(),
                // Display-only assigned/responsible worker; the user-id filter is deferred (the DTO carries
                // a name, not an id).
                AssignedUserName = p.Worker,
                IsActive = p.EndOfProject != true,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var projects = rows.Select(ToDto);

        // Apply the shared selector parity filters/order (dummy-number exclusion, active/include-closed,
        // job-type/status/free-text, number-descending). AssignedUserId is intentionally not applied here.
        return ProjectSummaryQuery.Apply(projects, query);
    }

    /// <inheritdoc />
    public async Task<ProjectSummaryDto?> GetProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectRow
            {
                Id = p.Id,
                Number = p.Number,
                Title = p.Title,
                NameAndNumber = p.NameAndNumber,
                PlaceName = p.Place != null ? p.Place.Title : null,
                CompanyName = p.Company != null ? p.Company.Title : null,
                StatusId = p.ProjectStatusId,
                StatusName = p.ProjectStatus != null ? p.ProjectStatus.Title : null,
                JobType = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectType != null && t.ProjectType.Title != null)
                    .Select(t => t.ProjectType!.Title)
                    .FirstOrDefault(),
                JobTypeIds = p.TypeOfProjectInProjects
                    .Where(t => t.ProjectTypeId != null)
                    .Select(t => t.ProjectTypeId!.Value)
                    .ToList(),
                AssignedUserName = p.Worker,
                IsActive = p.EndOfProject != true,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToDto(row);
    }

    private static ProjectSummaryDto ToDto(ProjectRow row) => new(
        ProjectId: row.Id,
        ProjectNumber: FormatNumber(row.Number),
        ProjectName: row.Title ?? string.Empty,
        PlaceName: NullIfBlank(row.PlaceName),
        CompanyName: NullIfBlank(row.CompanyName),
        JobType: NullIfBlank(row.JobType),
        Status: NullIfBlank(row.StatusName),
        AssignedUserName: NullIfBlank(row.AssignedUserName),
        IsActive: row.IsActive,
        StatusId: row.StatusId,
        JobTypeIds: row.JobTypeIds,
        ProjectLabelName: NullIfBlank(row.NameAndNumber));

    /// <summary>
    /// Formats the legacy <c>float?</c> project number as the selector's display string: an integer when
    /// there is no fractional part (e.g. <c>1042</c>), otherwise an invariant round-trip value. Empty when
    /// the number is missing.
    /// </summary>
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

    /// <summary>
    /// Flat projection of the columns the selector needs. Kept private so EF entities never escape this
    /// service and the DTO mapping (including number formatting) stays in memory.
    /// </summary>
    private sealed class ProjectRow
    {
        public int Id { get; init; }
        public float? Number { get; init; }
        public string? Title { get; init; }
        public string? NameAndNumber { get; init; }
        public string? PlaceName { get; init; }
        public string? CompanyName { get; init; }
        public int? StatusId { get; init; }
        public string? StatusName { get; init; }
        public string? JobType { get; init; }
        public List<int> JobTypeIds { get; init; } = [];
        public string? AssignedUserName { get; init; }
        public bool IsActive { get; init; }
    }
}
