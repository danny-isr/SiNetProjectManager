using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Fake/in-memory <see cref="IProjectFilterOptionsService"/> for design-time, tests, and hosts without a
/// database. Returns stable option lists independent of project search results.
/// </summary>
public sealed class FakeProjectFilterOptionsService : IProjectFilterOptionsService
{
    private static readonly ProjectFilterOptionsDto Options = new(
        Statuses:
        [
            new(1, "\u05E4\u05E2\u05D9\u05DC"),
            new(2, "\u05E1\u05D2\u05D5\u05E8"),
            new(3, "\u05DE\u05DE\u05EA\u05D9\u05DF \u05DC\u05E8\u05E9\u05D5\u05EA"),
        ],
        JobTypes:
        [
            new(1, "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD"),
            new(2, "\u05DE\u05E1\u05D7\u05E8"),
            new(3, "\u05DE\u05E9\u05E8\u05D3\u05D9\u05DD"),
            new(4, "\u05EA\u05E2\u05E9\u05D9\u05D9\u05D4"),
            new(5, "\u05E6\u05D9\u05D1\u05D5\u05E8\u05D9"),
        ],
        Users: Array.Empty<ProjectFilterOptionDto>());

    /// <inheritdoc />
    public Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Options);
    }
}
