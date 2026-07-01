using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Unit tests for <see cref="ProjectSummaryQuery"/>, the single source of truth for the shared Project
/// Selector's parity filtering/ordering (see <c>docs/PROJECTS.md</c> §5/§6). These are pure, DB-free
/// tests over already-materialized <see cref="ProjectSummaryDto"/> rows, so they lock in the exact
/// behavior shared by both the fake source and the real <c>SiNet.Infrastructure.Sql</c> source:
/// dummy-number exclusion, active/include-closed, exact Job Type / Status, free-text across
/// number/name/place/company, and default number-descending ordering.
/// </summary>
public sealed class ProjectSummaryQueryTests
{
    private static ProjectSummaryDto Project(
        int id,
        string number,
        string name = "Project",
        string? place = null,
        string? company = null,
        string? jobType = null,
        string? status = null,
        string? assignedUserName = null,
        bool isActive = true)
        => new(
            ProjectId: id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: place,
            CompanyName: company,
            JobType: jobType,
            Status: status,
            AssignedUserName: assignedUserName,
            IsActive: isActive);

    [Fact]
    public void Excludes_default_dummy_project_numbers()
    {
        var source = new[]
        {
            Project(1, "0", "dummy zero"),
            Project(2, "9999", "dummy reserved"),
            Project(3, "1042", "real"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery());

        Assert.Equal(new[] { 3 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Excludes_only_supplied_numbers_when_custom_set_provided()
    {
        var source = new[]
        {
            Project(1, "0", "kept because custom set only excludes 500"),
            Project(2, "500", "excluded"),
            Project(3, "1042", "kept"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(), new[] { "500" });

        Assert.Equal(new[] { 3, 1 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Hides_inactive_projects_by_default()
    {
        var source = new[]
        {
            Project(1, "1042", isActive: true),
            Project(2, "1041", isActive: false),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery());

        Assert.Equal(new[] { 1 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Includes_inactive_projects_when_IncludeClosed_is_set()
    {
        var source = new[]
        {
            Project(1, "1042", isActive: true),
            Project(2, "1041", isActive: false),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(IncludeClosed: true));

        // Number-descending order preserved across active and inactive.
        Assert.Equal(new[] { 1, 2 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Orders_by_project_number_descending_numerically()
    {
        // "1039" must sort below "1040"/"1042" numerically, not as text.
        var source = new[]
        {
            Project(1, "1039"),
            Project(2, "1042"),
            Project(3, "1040"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery());

        Assert.Equal(new[] { 2, 3, 1 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Filters_by_exact_job_type()
    {
        var source = new[]
        {
            Project(1, "1042", jobType: "מגורים"),
            Project(2, "1041", jobType: "מסחר"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(JobType: "מגורים"));

        Assert.Equal(new[] { 1 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Filters_by_exact_status()
    {
        var source = new[]
        {
            Project(1, "1042", status: "פעיל"),
            Project(2, "1041", status: "סגור", isActive: false),
        };

        var result = ProjectSummaryQuery.Apply(
            source,
            new ProjectSearchQuery(Status: "פעיל", IncludeClosed: true));

        Assert.Equal(new[] { 1 }, result.Select(p => p.ProjectId));
    }

    [Theory]
    [InlineData("1042", 1)] // by number
    [InlineData("הרצליה", 2)] // by place
    [InlineData("ספיר", 2)] // by company
    [InlineData("מגדלי", 1)] // by name
    public void Free_text_matches_number_name_place_or_company(string search, int expectedId)
    {
        var source = new[]
        {
            Project(1, "1042", name: "מגדלי הצפון", place: "תל אביב", company: "בני בניין"),
            Project(2, "1041", name: "משרדים", place: "הרצליה", company: "ספיר אדריכלות"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(SearchText: search));

        Assert.Equal(new[] { expectedId }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Free_text_is_case_insensitive_and_trimmed()
    {
        var source = new[] { Project(1, "1042", name: "North Tower") };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(SearchText: "  nORTh  "));

        Assert.Single(result);
        Assert.Equal(1, result[0].ProjectId);
    }

    [Fact]
    public void AssignedUserId_filter_is_not_applied_yet()
    {
        // The DTO carries a user NAME, not an id, so AssignedUserId must be ignored (deferred) rather
        // than silently dropping every row. Both projects remain.
        var source = new[]
        {
            Project(1, "1042", assignedUserName: "דני ישראל"),
            Project(2, "1041", assignedUserName: "רות כהן"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(AssignedUserId: 7));

        Assert.Equal(new[] { 1, 2 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Combines_text_status_and_include_closed_filters()
    {
        var source = new[]
        {
            Project(1, "1042", name: "מגדלי הצפון", status: "פעיל", isActive: true),
            Project(2, "1043", name: "מגדלי הדרום", status: "סגור", isActive: false),
            Project(3, "1044", name: "מרכז מסחרי", status: "פעיל", isActive: true),
        };

        var result = ProjectSummaryQuery.Apply(
            source,
            new ProjectSearchQuery(SearchText: "מגדלי", Status: "פעיל", IncludeClosed: true));

        Assert.Equal(new[] { 1 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void Does_not_mutate_the_source_sequence()
    {
        var source = new List<ProjectSummaryDto>
        {
            Project(1, "1039"),
            Project(2, "1042"),
        };

        _ = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery());

        // Original order/content untouched (Apply returns a new list).
        Assert.Equal(new[] { 1, 2 }, source.Select(p => p.ProjectId));
    }

    [Fact]
    public void Empty_source_returns_empty_result()
    {
        var result = ProjectSummaryQuery.Apply(Array.Empty<ProjectSummaryDto>(), new ProjectSearchQuery());

        Assert.Empty(result);
    }

    [Fact]
    public void MaxResults_caps_the_ordered_result_after_sorting()
    {
        // The cap must apply AFTER number-descending ordering, so the highest (newest) numbers win.
        var source = new[]
        {
            Project(1, "1001"),
            Project(2, "1005"),
            Project(3, "1003"),
            Project(4, "1004"),
        };

        var result = ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(MaxResults: 2));

        Assert.Equal(new[] { 2, 4 }, result.Select(p => p.ProjectId));
    }

    [Fact]
    public void MaxResults_null_or_non_positive_means_no_cap()
    {
        var source = new[] { Project(1, "1001"), Project(2, "1002"), Project(3, "1003") };

        Assert.Equal(3, ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(MaxResults: null)).Count);
        Assert.Equal(3, ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(MaxResults: 0)).Count);
        Assert.Equal(3, ProjectSummaryQuery.Apply(source, new ProjectSearchQuery(MaxResults: -5)).Count);
    }
}
