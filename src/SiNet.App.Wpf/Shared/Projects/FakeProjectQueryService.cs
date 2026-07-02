using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Fake/in-memory <see cref="IProjectQueryService"/> retained for design-time and tests
/// (see <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// <b>Fake data only.</b> It serves a small hard-coded list of <see cref="ProjectSummaryDto"/> and does
/// NOT touch the database, EF, or any external system. Filtering/sorting is delegated to the shared
/// <see cref="ProjectSummaryQuery"/> helper, so it reproduces the exact selector parity behavior used by
/// the real <c>SiNet.Infrastructure.Sql</c> source: results are sorted by project number descending,
/// dummy project numbers are excluded, and the free-text / job-type / status / include-closed filters are
/// applied. At runtime the real <c>SiNet.Infrastructure.Sql</c> implementation is registered instead of
/// this fake (behind the same interface); this type remains only for design-time/no-database hosts.
/// </para>
/// </summary>
public sealed class FakeProjectQueryService : IProjectQueryService
{
    private static readonly IReadOnlyList<ProjectSummaryDto> Projects =
    [
        new(1042, "1042", "\u05DE\u05D2\u05D3\u05DC\u05D9 \u05D4\u05E6\u05E4\u05D5\u05DF", "\u05EA\u05DC \u05D0\u05D1\u05D9\u05D1", "\u05D0\u05D1\u05E0\u05D9 \u05D1\u05E0\u05D9\u05D9\u05DF \u05D1\u05E2\u0022\u05DE", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true, StatusId: 1, JobTypeIds: [1]),
        new(1041, "1041", "\u05DE\u05E9\u05E8\u05D3\u05D9 \u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05E1\u05E4\u05D9\u05E8 \u05D0\u05D3\u05E8\u05D9\u05DB\u05DC\u05D5\u05EA", "\u05DE\u05E9\u05E8\u05D3\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", true, StatusId: 1, JobTypeIds: [3]),
        new(1040, "1040", "\u05E9\u05DB\u05D5\u05E0\u05EA \u05D4\u05D2\u05E0\u05D9\u05DD", "\u05E8\u05D0\u05E9\u05D5\u05DF \u05DC\u05E6\u05D9\u05D5\u05DF", "\u05D2\u05E8\u05D9\u05DF \u05D1\u05D9\u05DC\u05D3\u05D9\u05E0\u05D2", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05DE\u05DE\u05EA\u05D9\u05DF \u05DC\u05E8\u05E9\u05D5\u05EA", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true, StatusId: 3, JobTypeIds: [1]),
        new(1039, "1039", "\u05DE\u05E8\u05DB\u05D6 \u05DE\u05E1\u05D7\u05E8\u05D9 \u05E6\u05E4\u05D5\u05DF", "\u05D7\u05D9\u05E4\u05D4", "\u05E6\u05E4\u05D5\u05DF \u05D9\u05D6\u05DE\u05D5\u05EA", "\u05DE\u05E1\u05D7\u05E8", "\u05E4\u05E2\u05D9\u05DC", "\u05DE\u05E9\u05D4 \u05DC\u05D5\u05D9", true, StatusId: 1, JobTypeIds: [2]),
        new(1035, "1035", "\u05D1\u05D9\u05EA \u05E1\u05E4\u05E8 \u05D9\u05E1\u05D5\u05D3\u05D9", "\u05E4\u05EA\u05D7 \u05EA\u05E7\u05D5\u05D5\u05D4", "\u05E2\u05D9\u05E8\u05D9\u05D9\u05EA \u05E4\u05EA\u05D7 \u05EA\u05E7\u05D5\u05D5\u05D4", "\u05E6\u05D9\u05D1\u05D5\u05E8\u05D9", "\u05E1\u05D2\u05D5\u05E8", "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", false, StatusId: 2, JobTypeIds: [5]),
        new(1028, "1028", "\u05DE\u05D2\u05E8\u05E9 \u05EA\u05E2\u05E9\u05D9\u05D9\u05D4", "\u05D0\u05E9\u05D3\u05D5\u05D3", "\u05D3\u05E8\u05D5\u05DD \u05EA\u05E2\u05E9\u05D9\u05D9\u05D5\u05EA", "\u05EA\u05E2\u05E9\u05D9\u05D9\u05D4", "\u05E1\u05D2\u05D5\u05E8", "\u05DE\u05E9\u05D4 \u05DC\u05D5\u05D9", false, StatusId: 2, JobTypeIds: [4]),
    ];

    /// <inheritdoc />
    public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
        ProjectSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(query);

        // Delegate to the shared parity filter/sort so the fake behaves exactly like the real
        // SiNet.Infrastructure.Sql source (dummy-number exclusion, active/include-closed,
        // job-type/status/free-text filters, number-descending ordering).
        var ordered = ProjectSummaryQuery.Apply(Projects, query);

        return Task.FromResult(ordered);
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
}
