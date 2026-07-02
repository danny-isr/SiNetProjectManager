using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.Projects;

/// <summary>
/// Real, <b>read-only</b> <see cref="IProjectQueryService"/> backed by the existing SiNetSQL EF model
/// (see <c>docs/PROJECTS.md</c> §5/§6 and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c> §8a).
/// <para>
/// Browse mode (no search text) filters and caps in SQL so the full catalog is not materialized.
/// Search mode filters in SQL against the full source, then applies shared relevance ranking and
/// <c>MaxResults</c> in memory via <see cref="ProjectSummaryQuery"/>.
/// </para>
/// </summary>
public sealed class ProjectQueryService : IProjectQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

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

        var sw = Stopwatch.StartNew();
        var hasSearch = !string.IsNullOrWhiteSpace(query.SearchText);
        var maxResults = query.MaxResults is int cap && cap > 0 ? cap : (int?)null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var filtered = ApplyBaseFilters(db.Projects.AsNoTracking(), query);

        if (!hasSearch && maxResults.HasValue)
        {
            var rows = await filtered
                .OrderByDescending(p => p.Number ?? float.MinValue)
                .ThenByDescending(p => p.Id)
                .Take(maxResults.Value)
                .Select(ProjectRowProjection)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var browseMs = sw.ElapsedMilliseconds;
            var browseResults = ProjectSummaryQuery.Apply(rows.Select(ToDto), query with { MaxResults = null });

            Debug.WriteLine(
                $"[PERF] ProjectQueryService.SearchProjectsAsync (browse): SQL capped at {maxResults.Value}, " +
                $"returned {browseResults.Count} row(s) in {browseMs} ms.");

            return browseResults;
        }

        if (hasSearch)
        {
            filtered = ApplySearchTokens(filtered, query.SearchText!.Trim());
        }

        var matchingRows = await filtered
            .Select(ProjectRowProjection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var loadedMs = sw.ElapsedMilliseconds;
        var results = ProjectSummaryQuery.Apply(matchingRows.Select(ToDto), query);

        Debug.WriteLine(
            $"[PERF] ProjectQueryService.SearchProjectsAsync ({(hasSearch ? "search" : "browse-uncapped")}): " +
            $"matched {matchingRows.Count} row(s) from DB in {loadedMs} ms, " +
            $"returned {results.Count} after rank/cap in {sw.ElapsedMilliseconds} ms.");

        return results;
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
            .Select(ProjectRowProjection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToDto(row);
    }

    private static IQueryable<Project> ApplyBaseFilters(IQueryable<Project> source, ProjectSearchQuery query)
    {
        var results = source.Where(p => p.NameAndNumber != null);

        // Dummy/reserved numbers — parity with ProjectSummaryQuery.DefaultExcludedNumbers.
        results = results.Where(p => p.Number == null || (p.Number != 0 && p.Number != 9999));

        if (!query.IncludeClosed)
        {
            results = results.Where(p => p.EndOfProject != true);
        }

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            var jobType = query.JobType;
            results = results.Where(p => p.TypeOfProjectInProjects.Any(t =>
                t.ProjectType != null && t.ProjectType.Title == jobType));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status;
            results = results.Where(p => p.ProjectStatus != null && p.ProjectStatus.Title == status);
        }

        return results;
    }

    private static IQueryable<Project> ApplySearchTokens(IQueryable<Project> source, string searchText)
    {
        var tokens = ProjectSummaryQuery.SplitSearchTokens(searchText);
        foreach (var token in tokens)
        {
            var t = token;
            source = source.Where(p =>
                (p.NameAndNumber != null && p.NameAndNumber.Contains(t))
                || (p.Title != null && p.Title.Contains(t))
                || (p.Place != null && p.Place.Title != null && p.Place.Title.Contains(t))
                || (p.Company != null && p.Company.Title != null && p.Company.Title.Contains(t)));
        }

        return source;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Project, ProjectRow>> ProjectRowProjection =
        p => new ProjectRow
        {
            Id = p.Id,
            Number = p.Number,
            Title = p.Title,
            PlaceName = p.Place != null ? p.Place.Title : null,
            CompanyName = p.Company != null ? p.Company.Title : null,
            StatusName = p.ProjectStatus != null ? p.ProjectStatus.Title : null,
            JobType = p.TypeOfProjectInProjects
                .Where(t => t.ProjectType != null && t.ProjectType.Title != null)
                .Select(t => t.ProjectType!.Title)
                .FirstOrDefault(),
            AssignedUserName = p.Worker,
            IsActive = p.EndOfProject != true,
        };

    private static ProjectSummaryDto ToDto(ProjectRow row) => new(
        ProjectId: row.Id,
        ProjectNumber: FormatNumber(row.Number),
        ProjectName: row.Title ?? string.Empty,
        PlaceName: NullIfBlank(row.PlaceName),
        CompanyName: NullIfBlank(row.CompanyName),
        JobType: NullIfBlank(row.JobType),
        Status: NullIfBlank(row.StatusName),
        AssignedUserName: NullIfBlank(row.AssignedUserName),
        IsActive: row.IsActive);

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

    private sealed class ProjectRow
    {
        public int Id { get; init; }
        public float? Number { get; init; }
        public string? Title { get; init; }
        public string? PlaceName { get; init; }
        public string? CompanyName { get; init; }
        public string? StatusName { get; init; }
        public string? JobType { get; init; }
        public string? AssignedUserName { get; init; }
        public bool IsActive { get; init; }
    }
}
