using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;

namespace SiNetSQL.Services.Projects;

/// <summary>
/// Real, <b>read-only</b> <see cref="IProjectFilterOptionsService"/> backed by the existing SiNetSQL
/// model. Options are limited to status and job-type values that appear on selectable projects
/// (<c>NameAndNumber</c> present), matching legacy selector semantics.
/// </summary>
public sealed class ProjectFilterOptionsService : IProjectFilterOptionsService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public ProjectFilterOptionsService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var selectableProjects = db.Projects
            .AsNoTracking()
            .Where(p => p.NameAndNumber != null);

        var usedStatusIds = await selectableProjects
            .Where(p => p.ProjectStatusId != null)
            .Select(p => p.ProjectStatusId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statuses = await db.ProjectStatuses
            .AsNoTracking()
            .Where(s => s.IsActive
                && usedStatusIds.Contains(s.Id)
                && s.Title != null
                && s.Title != string.Empty)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Title)
            .Select(s => new ProjectFilterOptionDto(s.Id, s.Title!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var usedJobTypeIds = await db.TypeOfProjectInProjects
            .AsNoTracking()
            .Where(t => t.ProjectTypeId != null
                && t.Project != null
                && t.Project.NameAndNumber != null)
            .Select(t => t.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(j => usedJobTypeIds.Contains(j.Id)
                && j.Title != null
                && j.Title != string.Empty)
            .OrderBy(j => j.Title)
            .Select(j => new ProjectFilterOptionDto(j.Id, j.Title!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // User filter semantics are not yet defined for the new selector — return an empty list so
        // the UI can hide the control instead of showing a misleading partial list.
        return new ProjectFilterOptionsDto(statuses, jobTypes, Array.Empty<ProjectFilterOptionDto>());
    }
}
