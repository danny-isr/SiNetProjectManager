using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;

namespace SiNetSQL.Services.Projects;

/// <summary>
/// Real, <b>read-only</b> <see cref="IProjectFilterOptionsService"/> backed by the existing SiNetSQL
/// reference tables. No writes, no schema changes.
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

        var statuses = await db.ProjectStatuses
            .AsNoTracking()
            .Where(s => s.IsActive && s.Title != null && s.Title != string.Empty)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Title)
            .Select(s => new ProjectFilterOptionDto(s.Id, s.Title!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Title != null && j.Title != string.Empty)
            .OrderBy(j => j.Title)
            .Select(j => new ProjectFilterOptionDto(j.Id, j.Title!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // User filter semantics are not yet defined for the new selector — return an empty list so
        // the UI can hide the control instead of showing a misleading partial list.
        return new ProjectFilterOptionsDto(statuses, jobTypes, Array.Empty<ProjectFilterOptionDto>());
    }
}
