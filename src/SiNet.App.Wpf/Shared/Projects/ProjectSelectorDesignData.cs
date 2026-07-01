using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Design-time sample data for the shared Project Selector (see <c>docs/PROJECTS.md</c> §5).
/// <para>
/// The selector's view model already renders standalone at design time via its parameterless
/// constructor (backed by <see cref="FakeProjectQueryService"/> and
/// <see cref="InMemoryCurrentProjectContext"/>). This type exposes a few ready-made
/// <see cref="ProjectSummaryDto"/> rows for lightweight previews/tests that want project rows without
/// constructing a view model. It is fake/presentation data only — no DB, no EF.
/// </para>
/// </summary>
public static class ProjectSelectorDesignData
{
    /// <summary>A small set of sample projects for design-time rendering and previews.</summary>
    public static IReadOnlyList<ProjectSummaryDto> SampleProjects { get; } =
    [
        new(1042, "1042", "\u05DE\u05D2\u05D3\u05DC\u05D9 \u05D4\u05E6\u05E4\u05D5\u05DF", "\u05EA\u05DC \u05D0\u05D1\u05D9\u05D1", "\u05D0\u05D1\u05E0\u05D9 \u05D1\u05E0\u05D9\u05D9\u05DF \u05D1\u05E2\u0022\u05DE", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true),
        new(1041, "1041", "\u05DE\u05E9\u05E8\u05D3\u05D9 \u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05D4\u05E8\u05E6\u05DC\u05D9\u05D4", "\u05E1\u05E4\u05D9\u05E8 \u05D0\u05D3\u05E8\u05D9\u05DB\u05DC\u05D5\u05EA", "\u05DE\u05E9\u05E8\u05D3\u05D9\u05DD", "\u05E4\u05E2\u05D9\u05DC", "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", true),
        new(1040, "1040", "\u05E9\u05DB\u05D5\u05E0\u05EA \u05D4\u05D2\u05E0\u05D9\u05DD", "\u05E8\u05D0\u05E9\u05D5\u05DF \u05DC\u05E6\u05D9\u05D5\u05DF", "\u05D2\u05E8\u05D9\u05DF \u05D1\u05D9\u05DC\u05D3\u05D9\u05E0\u05D2", "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", "\u05DE\u05DE\u05EA\u05D9\u05DF \u05DC\u05E8\u05E9\u05D5\u05EA", "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", true),
    ];
}
