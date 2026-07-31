using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectFileNameBuilderTests
{
    [Fact]
    public void Build_produces_canonical_name_that_round_trips_through_the_parser()
    {
        var name = ProjectFileNameBuilder.Build(5, 3, 7, "1", 2, "Plan", "drawing.DWG");

        Assert.Equal("(5)-3-7-1-2-Plan.dwg", name);
        var parsed = ProjectFileNameParser.TryParse(name);
        Assert.NotNull(parsed);
        Assert.Equal(5, parsed!.ProjectNumber);
        Assert.Equal(3, parsed.ProjectType);
        Assert.Equal(7, parsed.Number);
        Assert.Equal("1", parsed.Alternative);
        Assert.Equal(2, parsed.Version);
        Assert.Equal("Plan", parsed.BaseName);
        Assert.Equal("dwg", parsed.Extension);
    }

    [Fact]
    public void Build_caps_base_name_to_max_length()
    {
        var tooLong = new string('A', ProjectFileNameBuilder.MaxBaseNameLength + 5);
        var name = ProjectFileNameBuilder.Build(1, 1, 1, "1", 1, tooLong, "x.pdf");
        var parsed = ProjectFileNameParser.TryParse(name);
        Assert.Equal(new string('A', ProjectFileNameBuilder.MaxBaseNameLength), parsed!.BaseName);
        Assert.Equal(ProjectFileNameBuilder.MaxBaseNameLength, parsed.BaseName.Length);
    }

    [Fact]
    public void Build_keeps_quote_send_title_intact_under_raised_cap()
    {
        var name = ProjectFileNameBuilder.Build(3142, 9, 76, "1", 1, "הצעה_לשליחה", "q.pdf");
        var parsed = ProjectFileNameParser.TryParse(name);
        Assert.Equal("הצעה_לשליחה", parsed!.BaseName);
        Assert.Equal("(3142)-9-76-1-1-הצעה_לשליחה.pdf", name);
    }

    [Fact]
    public void Build_defaults_empty_alternative_to_one_and_nonpositive_version_to_one()
    {
        var name = ProjectFileNameBuilder.Build(2, 4, 9, alternative: "", version: 0, "Doc", "f.pdf");
        Assert.Equal("(2)-4-9-1-1-Doc.pdf", name);
    }

    [Fact]
    public void Build_returns_original_name_when_identity_incomplete()
    {
        Assert.Equal("orig.pdf", ProjectFileNameBuilder.Build(0, 1, 1, "1", 1, "T", "orig.pdf"));
        Assert.Equal("orig.pdf", ProjectFileNameBuilder.Build(5, 1, 0, "1", 1, "T", "orig.pdf"));
    }

    [Fact]
    public void BuildNextVersion_keeps_identity_and_advances_version()
    {
        var existing = ProjectFileNameParser.TryParse("(5)-3-7-A-2-North-Wing.pdf")!;
        var next = ProjectFileNameBuilder.BuildNextVersion(existing, 3);
        Assert.Equal("(5)-3-7-A-3-North-Wing.pdf", next);
    }

    [Fact]
    public void BuildNextVersion_auto_increments_when_next_is_nonpositive()
    {
        var existing = ProjectFileNameParser.TryParse("(5)-3-7-1-2-Plan.dwg")!;
        Assert.Equal("(5)-3-7-1-3-Plan.dwg", ProjectFileNameBuilder.BuildNextVersion(existing, 0));
    }
}
