using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Fake/in-memory <see cref="IProjectQueryService"/> for the first Project Context slice
/// (see <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// <b>Fake data only.</b> It serves a small hard-coded list of <see cref="ProjectSummaryDto"/> and does
/// NOT touch the database, EF, or any external system. It reproduces the shared selector's parity
/// behavior so the UI feels real: results are sorted by project number descending, dummy project
/// numbers are excluded, and the free-text / job-type / status / user / include-closed filters are
/// applied. A real <c>SiNet.Infrastructure.Sql</c> (or <c>SiNet.LegacyBridge</c>) implementation
/// replaces this later behind the same interface.
/// </para>
/// </summary>
public sealed class FakeProjectQueryService : IProjectQueryService
{
    // Dummy/reserved project numbers that must never appear in the selector (parity with legacy
    // ExcludedNumbers behavior). Kept as strings because the DTO number is a display string.
    private static readonly HashSet<string> ExcludedNumbers = new(StringComparer.Ordinal)
    {
        "0",
        "9999",
    };

    private static readonly IReadOnlyList<ProjectSummaryDto> Projects =
    [
        new(1042, "1042", "\u05DE\u05D2\u05D3\u05DC\u05D9 \u05D4\u05E6\u05E4\u05D5\u05DF", "\u05EA\u05DC \u05D0\u05D1\u05D9\u05D1", "\u05D0\u05D1\u05E0\u05D9 \u05D1\u05E0\u05D9\u05D9\u05DF \u05D1\u05E2\u0022\u05DE", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true),
        new(1041, "1041", "\u05DE\u05E9\u05E8\u05D3\u05D9 \u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05E1\u05E4\u05D9\u05E8 \u05D0\u05D3\u05E8\u05D9\u05DB\u05DC\u05D5\u05EA", "\u05DE\u05E9\u05E8\u05D3\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", true),
        new(1040, "1040", "\u05E9\u05DB\u05D5\u05E0\u05EA \u05D4\u05D2\u05E0\u05D9\u05DD", "\u05E8\u05D0\u05E9\u05D5\u05DF \u05DC\u05E6\u05D9\u05D5\u05DF", "\u05D2\u05E8\u05D9\u05DF \u05D1\u05D9\u05DC\u05D3\u05D9\u05E0\u05D2", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05DE\u05DE\u05EA\u05D9\u05DF \u05DC\u05E8\u05E9\u05D5\u05EA", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true),
        new(1039, "1039", "\u05DE\u05E8\u05DB\u05D6 \u05DE\u05E1\u05D7\u05E8\u05D9 \u05E6\u05E4\u05D5\u05DF", "\u05D7\u05D9\u05E4\u05D4", "\u05E6\u05E4\u05D5\u05DF \u05D9\u05D6\u05DE\u05D5\u05EA", "\u05DE\u05E1\u05D7\u05E8", "\u05E4\u05E2\u05D9\u05DC", "\u05DE\u05E9\u05D4 \u05DC\u05D5\u05D9", true),
        new(1035, "1035", "\u05D1\u05D9\u05EA \u05E1\u05E4\u05E8 \u05D9\u05E1\u05D5\u05D3\u05D9", "\u05E4\u05EA\u05D7 \u05EA\u05E7\u05D5\u05D5\u05D4", "\u05E2\u05D9\u05E8\u05D9\u05D9\u05EA \u05E4\u05EA\u05D7 \u05EA\u05E7\u05D5\u05D5\u05D4", "\u05E6\u05D9\u05D1\u05D5\u05E8\u05D9", "\u05E1\u05D2\u05D5\u05E8", "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", false),
        new(1028, "1028", "\u05DE\u05D2\u05E8\u05E9 \u05EA\u05E2\u05E9\u05D9\u05D9\u05D4", "\u05D0\u05E9\u05D3\u05D5\u05D3", "\u05D3\u05E8\u05D5\u05DD \u05EA\u05E2\u05E9\u05D9\u05D9\u05D5\u05EA", "\u05EA\u05E2\u05E9\u05D9\u05D9\u05D4", "\u05E1\u05D2\u05D5\u05E8", "\u05DE\u05E9\u05D4 \u05DC\u05D5\u05D9", false),
    ];

    /// <inheritdoc />
    public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
        ProjectSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<ProjectSummaryDto> results = Projects
            .Where(p => !ExcludedNumbers.Contains(p.ProjectNumber));

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

        // Parity ordering: project number descending (newest first). Numbers are display strings, so
        // parse for a stable numeric sort and fall back to ordinal for anything non-numeric.
        var ordered = results
            .OrderByDescending(p => long.TryParse(p.ProjectNumber, out var n) ? n : long.MinValue)
            .ThenByDescending(p => p.ProjectNumber, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(ordered);
    }

    /// <inheritdoc />
    public Task<ProjectSummaryDto?> GetProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var match = Projects.FirstOrDefault(p => p.ProjectId == projectId);
        return Task.FromResult(match);
    }

    private static bool MatchesText(ProjectSummaryDto p, string text) =>
        p.ProjectNumber.Contains(text, StringComparison.OrdinalIgnoreCase)
        || p.ProjectName.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (p.PlaceName?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || (p.CompanyName?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
}
