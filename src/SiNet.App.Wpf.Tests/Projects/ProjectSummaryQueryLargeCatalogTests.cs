using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Large-catalog tests for <see cref="ProjectSummaryQuery"/>: proves MaxResults is a display cap
/// applied after filtering on the full source, not a pre-filter that limits search (see
/// <c>docs/PROJECTS.md</c> search source vs display limit).
/// </summary>
public sealed class ProjectSummaryQueryLargeCatalogTests
{
    private const int CatalogSize = 2500;
    private const int DisplayCap = 200;

    private static ProjectSummaryDto Project(
        int id,
        string number,
        string name = "Project",
        string? place = null,
        string? company = null)
        => new(
            ProjectId: id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: place,
            CompanyName: company,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

    private static IReadOnlyList<ProjectSummaryDto> BuildCatalog()
        => Enumerable.Range(1, CatalogSize)
            .Select(i => Project(i, i.ToString(), name: $"Project {i}", place: i == 1 ? "\u05E8\u05E2\u05E0\u05E0\u05D4" : "\u05EA\u05DC \u05D0\u05D1\u05D9\u05D1"))
            .ToArray();

    [Fact]
    public void Browse_mode_returns_at_most_MaxResults_newest_projects()
    {
        var catalog = BuildCatalog();

        var result = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(MaxResults: DisplayCap));

        Assert.Equal(DisplayCap, result.Count);
        Assert.Equal("2500", result[0].ProjectNumber);
        Assert.Equal("2301", result[^1].ProjectNumber);
        Assert.DoesNotContain(result, p => p.ProjectNumber == "1");
    }

    [Fact]
    public void Search_by_exact_old_project_number_finds_project_outside_initial_browse_cap()
    {
        var catalog = BuildCatalog();

        var result = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "1", MaxResults: DisplayCap));

        Assert.Contains(result, p => p.ProjectNumber == "1");
        Assert.Equal("1", result[0].ProjectNumber);
    }

    [Fact]
    public void Search_by_name_finds_old_project_outside_initial_browse_cap()
    {
        var catalog = BuildCatalog();

        var result = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "Project 1", MaxResults: DisplayCap));

        Assert.Contains(result, p => p.ProjectId == 1);
    }

    [Fact]
    public void Search_by_place_and_number_finds_old_project()
    {
        var catalog = BuildCatalog();

        var result = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "\u05E8\u05E2\u05E0\u05E0\u05D4 1", MaxResults: DisplayCap));

        Assert.Single(result);
        Assert.Equal("1", result[0].ProjectNumber);
    }

    [Fact]
    public void MaxResults_applied_after_filtering_not_before()
    {
        var catalog = BuildCatalog();

        var filteredOnly = catalog.Where(p => ProjectSummaryQuery.MatchesText(p, "1")).ToList();
        Assert.True(filteredOnly.Count > DisplayCap, "fixture must exceed cap after filter");

        var result = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "1", MaxResults: DisplayCap));

        Assert.Equal(DisplayCap, result.Count);
        Assert.Equal("1", result[0].ProjectNumber);
    }

    [Fact]
    public void Multi_word_search_works_in_either_order_for_old_project()
    {
        var catalog = BuildCatalog();

        var forward = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "\u05E8\u05E2\u05E0\u05E0\u05D4 1", MaxResults: DisplayCap));

        var reverse = ProjectSummaryQuery.Apply(
            catalog,
            new ProjectSearchQuery(SearchText: "1 \u05E8\u05E2\u05E0\u05E0\u05D4", MaxResults: DisplayCap));

        Assert.Equal("1", forward[0].ProjectNumber);
        Assert.Equal("1", reverse[0].ProjectNumber);
    }

    [Fact]
    public void GetSearchRank_prefers_exact_number_match_over_substring_match()
    {
        var exact = Project(1, "1", name: "Old");
        var substring = Project(2, "1042", name: "Newer with digit");

        var rankExact = ProjectSummaryQuery.GetSearchRank(exact, "1");
        var rankSubstring = ProjectSummaryQuery.GetSearchRank(substring, "1");

        Assert.True(rankExact > rankSubstring);
    }
}
