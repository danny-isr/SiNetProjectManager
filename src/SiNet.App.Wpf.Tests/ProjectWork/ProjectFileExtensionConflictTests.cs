using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectFileExtensionConflictTests
{
    [Fact]
    public void FindConflict_flags_same_base_name_with_different_extension()
    {
        var conflict = ProjectFileExtensionConflict.FindConflict(
            "(5)-3-7-1-1-Plan.dwg",
            new[] { "(5)-3-7-1-1-Plan.pdf", "other.txt" });

        Assert.Equal("(5)-3-7-1-1-Plan.pdf", conflict);
    }

    [Fact]
    public void FindConflict_ignores_same_extension_including_self()
    {
        var conflict = ProjectFileExtensionConflict.FindConflict(
            "(5)-3-7-1-1-Plan.dwg",
            new[] { "(5)-3-7-1-1-Plan.dwg" });

        Assert.Null(conflict);
    }

    [Fact]
    public void FindConflict_is_case_insensitive_on_base_and_extension()
    {
        Assert.Null(ProjectFileExtensionConflict.FindConflict("Plan.DWG", new[] { "plan.dwg" }));
        Assert.Equal("PLAN.pdf", ProjectFileExtensionConflict.FindConflict("plan.dwg", new[] { "PLAN.pdf" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindConflict_null_safe(string? candidate)
    {
        Assert.Null(ProjectFileExtensionConflict.FindConflict(candidate, new[] { "a.dwg" }));
        Assert.Null(ProjectFileExtensionConflict.FindConflict("a.dwg", null));
    }

    [Fact]
    public void FindConflict_no_match_when_base_names_differ()
        => Assert.Null(ProjectFileExtensionConflict.FindConflict(
            "(5)-3-7-1-2-Plan.dwg",
            new[] { "(5)-3-7-1-1-Plan.pdf" }));
}
