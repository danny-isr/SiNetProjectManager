namespace SiNet.Application.Projects;

/// <summary>
/// DB-free, in-memory implementation of the shared Project Selector's parity filtering and ordering
/// over already-materialized <see cref="ProjectSummaryDto"/> rows (see <c>docs/PROJECTS.md</c> §5/§6).
/// </summary>
public static class ProjectSummaryQuery
{
    public static readonly IReadOnlyCollection<string> DefaultExcludedNumbers = new[] { "0", "9999" };

    public static IReadOnlyList<ProjectSummaryDto> Apply(
        IEnumerable<ProjectSummaryDto> source,
        ProjectSearchQuery query)
        => Apply(source, query, DefaultExcludedNumbers);

    public static IReadOnlyList<ProjectSummaryDto> Apply(
        IEnumerable<ProjectSummaryDto> source,
        ProjectSearchQuery query,
        IReadOnlyCollection<string> excludedNumbers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(excludedNumbers);

        var excluded = excludedNumbers as HashSet<string>
            ?? new HashSet<string>(excludedNumbers, StringComparer.Ordinal);

        IEnumerable<ProjectSummaryDto> results = source
            .Where(p => p is not null)
            .Where(p => !excluded.Contains(p.ProjectNumber));

        if (!query.IncludeClosed)
        {
            results = results.Where(p => p.IsActive);
        }

        if (query.StatusId is int statusId)
        {
            results = results.Where(p => p.StatusId == statusId);
        }
        else if (!string.IsNullOrWhiteSpace(query.Status))
        {
            results = results.Where(p => string.Equals(p.Status, query.Status, StringComparison.Ordinal));
        }

        if (query.JobTypeId is int jobTypeId)
        {
            results = results.Where(p => p.JobTypeIds?.Contains(jobTypeId) == true);
        }
        else if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            results = results.Where(p => string.Equals(p.JobType, query.JobType, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            results = results.Where(p => MatchesText(p, text));
        }

        var ordered = OrderResults(results, query);

        if (query.MaxResults is int max && max > 0)
        {
            return ordered.Take(max).ToList();
        }

        return ordered.ToList();
    }

    public static IReadOnlyList<string> SplitSearchTokens(string searchText)
    {
        ArgumentNullException.ThrowIfNull(searchText);

        return searchText
            .Split([' ', '\t', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToArray();
    }

    public static bool MatchesText(ProjectSummaryDto project, string text)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(text);

        var tokens = SplitSearchTokens(text.Trim());
        if (tokens.Count == 0)
        {
            return true;
        }

        return tokens.All(token => TokenMatches(project, token));
    }

    public static int GetSearchRank(ProjectSummaryDto project, string searchText)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(searchText);

        var rank = 0;
        foreach (var token in SplitSearchTokens(searchText.Trim()))
        {
            if (string.Equals(project.ProjectNumber, token, StringComparison.OrdinalIgnoreCase))
            {
                rank += 1000;
                continue;
            }

            if (project.ProjectNumber.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                rank += 100;
            }

            if (project.ProjectName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                rank += 50;
            }

            if (project.PlaceName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
            {
                rank += 40;
            }

            if (project.CompanyName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
            {
                rank += 30;
            }
        }

        return rank;
    }

    private static bool TokenMatches(ProjectSummaryDto project, string token)
        => project.ProjectNumber.Contains(token, StringComparison.OrdinalIgnoreCase)
            || project.ProjectName.Contains(token, StringComparison.OrdinalIgnoreCase)
            || (project.PlaceName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
            || (project.CompanyName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false);

    private static IEnumerable<ProjectSummaryDto> OrderResults(
        IEnumerable<ProjectSummaryDto> results,
        ProjectSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            return results
                .OrderByDescending(p => GetSearchRank(p, text))
                .ThenByDescending(p => ParseNumber(p.ProjectNumber))
                .ThenByDescending(p => p.ProjectNumber, StringComparer.Ordinal);
        }

        return results
            .OrderByDescending(p => ParseNumber(p.ProjectNumber))
            .ThenByDescending(p => p.ProjectNumber, StringComparer.Ordinal);
    }

    private static long ParseNumber(string number)
        => long.TryParse(number, out var n) ? n : long.MinValue;
}
