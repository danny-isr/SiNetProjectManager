namespace SiNet.Application.Projects;

/// <summary>
/// DB-free, in-memory implementation of the shared Project Selector's parity filtering and ordering
/// over already-materialized <see cref="ProjectSummaryDto"/> rows (see <c>docs/PROJECTS.md</c> §5/§6).
/// <para>
/// This is the single source of truth for the selector's behavior so every
/// <see cref="IProjectQueryService"/> implementation (the fake in-memory source and the real
/// <c>SiNet.Infrastructure.Sql</c> source) applies exactly the same rules:
/// </para>
/// <list type="bullet">
/// <item><description>exclude dummy/reserved project numbers,</description></item>
/// <item><description>hide closed/inactive projects unless <c>IncludeClosed</c> is set,</description></item>
/// <item><description>free-text search across number / name / place / company,</description></item>
/// <item><description>Job Type and Status filters (exact match on the surfaced display value),</description></item>
/// <item><description>default sort by project number descending (newest first).</description></item>
/// </list>
/// <para>
/// It operates purely on the DTO projection and never touches EF, a <c>DbContext</c>, or the database,
/// which keeps the real SQL source thin and makes the parity behavior unit-testable without a database.
/// The <c>AssignedUserId</c> filter is intentionally not applied here because the display DTO carries a
/// user <em>name</em>, not an id; user-id filtering is deferred to a later slice (see <c>docs/PROJECTS.md</c>).
/// </para>
/// </summary>
public static class ProjectSummaryQuery
{
    /// <summary>
    /// Dummy/reserved project numbers that must never appear in the selector (parity with the legacy
    /// <c>ExcludedNumbers</c> behavior). Compared against the DTO's formatted number string.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultExcludedNumbers = new[] { "0", "9999" };

    /// <summary>
    /// Applies the selector's parity filters and ordering to <paramref name="source"/> using
    /// <see cref="DefaultExcludedNumbers"/> for dummy-number exclusion.
    /// </summary>
    /// <param name="source">The already-loaded project rows to filter/sort. Never mutated.</param>
    /// <param name="query">The search/filter criteria; an empty query returns the default active list.</param>
    /// <returns>A new ordered list; never <see langword="null"/>.</returns>
    public static IReadOnlyList<ProjectSummaryDto> Apply(
        IEnumerable<ProjectSummaryDto> source,
        ProjectSearchQuery query)
        => Apply(source, query, DefaultExcludedNumbers);

    /// <summary>
    /// Applies the selector's parity filters and ordering to <paramref name="source"/> using the
    /// supplied <paramref name="excludedNumbers"/> for dummy-number exclusion.
    /// </summary>
    /// <param name="source">The already-loaded project rows to filter/sort. Never mutated.</param>
    /// <param name="query">The search/filter criteria; an empty query returns the default active list.</param>
    /// <param name="excludedNumbers">Formatted project-number strings to exclude (dummy/reserved numbers).</param>
    /// <returns>A new ordered list; never <see langword="null"/>.</returns>
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

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            results = results.Where(p => string.Equals(p.JobType, query.JobType, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            results = results.Where(p => string.Equals(p.Status, query.Status, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            results = results.Where(p => MatchesText(p, text));
        }

        var ordered = OrderResults(results, query);

        // Display cap only — applied after filter + order on the full search source.
        if (query.MaxResults is int max && max > 0)
        {
            return ordered.Take(max).ToList();
        }

        return ordered.ToList();
    }

    /// <summary>
    /// Orders filtered projects for display. Browse mode: number descending. Search mode: relevance
    /// rank first (exact number match on a token wins over substring matches) then number descending,
    /// so <c>Take(MaxResults)</c> does not drop old projects such as number <c>1</c> when searching
    /// <c>"1"</c>.
    /// </summary>
    public static IReadOnlyList<ProjectSummaryDto> OrderResults(
        IEnumerable<ProjectSummaryDto> source,
        ProjectSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.SearchText))
        {
            return source
                .OrderByDescending(NumberSortKey)
                .ThenByDescending(p => p.ProjectNumber, StringComparer.Ordinal)
                .ToList();
        }

        var text = query.SearchText.Trim();
        return source
            .OrderByDescending(p => GetSearchRank(p, text))
            .ThenByDescending(NumberSortKey)
            .ThenByDescending(p => p.ProjectNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Relevance score for search ordering. Higher = shown first. Exact <see cref="ProjectSummaryDto.ProjectNumber"/>
    /// match on a token ranks above prefix/substring matches.
    /// </summary>
    public static int GetSearchRank(ProjectSummaryDto project, string searchText)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(searchText);

        var tokens = SplitSearchTokens(searchText.Trim());
        if (tokens.Length == 0)
        {
            return 0;
        }

        var rank = 0;
        foreach (var token in tokens)
        {
            if (string.Equals(project.ProjectNumber, token, StringComparison.OrdinalIgnoreCase))
            {
                rank += 10_000;
            }
            else if (project.ProjectNumber.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                rank += 1_000;
            }
            else if (TokenMatchesAnyField(project, token))
            {
                rank += 100;
            }
        }

        return rank;
    }

    private static long NumberSortKey(ProjectSummaryDto project)
        => long.TryParse(project.ProjectNumber, out var n) ? n : long.MinValue;

    /// <summary>
    /// Token separators for free-text search (parity with legacy
    /// <c>SearchableProjectSelector</c>: space, tab, newline, comma, semicolon).
    /// </summary>
    public static readonly char[] SearchTokenSeparators = [' ', '\t', '\r', '\n', ',', ';'];

    /// <summary>
    /// Splits <paramref name="text"/> into non-empty search tokens.
    /// </summary>
    public static string[] SplitSearchTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Split(
            SearchTokenSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Returns <see langword="true"/> when every token in <paramref name="text"/> appears in at least
    /// one of the project's number, name, place, or company fields (case-insensitive substring). Token
    /// order does not matter. Mirrors legacy <c>FilterProperties = NameAndNumber,Title,Place.Title,Company.Title</c>.
    /// </summary>
    public static bool MatchesText(ProjectSummaryDto project, string text)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(text);

        var tokens = SplitSearchTokens(text.Trim());
        if (tokens.Length == 0)
        {
            return true;
        }

        foreach (var token in tokens)
        {
            if (!TokenMatchesAnyField(project, token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TokenMatchesAnyField(ProjectSummaryDto project, string token)
        => project.ProjectNumber.Contains(token, StringComparison.OrdinalIgnoreCase)
            || project.ProjectName.Contains(token, StringComparison.OrdinalIgnoreCase)
            || (project.PlaceName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)
            || (project.CompanyName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false);
}
