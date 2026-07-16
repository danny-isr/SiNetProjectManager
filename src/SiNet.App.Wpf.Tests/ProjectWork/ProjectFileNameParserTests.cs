using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectFileNameParserTests
{
    [Fact]
    public void TryParse_canonical_name_extracts_all_components()
    {
        var parsed = ProjectFileNameParser.TryParse("(5)-3-7-1-2-Site Plan.dwg");

        Assert.NotNull(parsed);
        Assert.Equal(5, parsed!.ProjectNumber);
        Assert.Equal(3, parsed.ProjectType);
        Assert.Equal(7, parsed.Number);
        Assert.Equal("1", parsed.Alternative);
        Assert.Equal(2, parsed.Version);
        Assert.Equal("Site Plan", parsed.BaseName);
        Assert.Equal("dwg", parsed.Extension);
    }

    [Fact]
    public void TryParse_keeps_dashes_in_base_name()
    {
        var parsed = ProjectFileNameParser.TryParse("(12)-1-4-A-3-North-East-Wing.pdf");

        Assert.NotNull(parsed);
        Assert.Equal("A", parsed!.Alternative);
        Assert.Equal("North-East-Wing", parsed.BaseName);
        Assert.Equal("pdf", parsed.Extension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("random.txt")]
    [InlineData("(5)-3-7-1-2-NoExtension")]
    [InlineData("(abc)-3-7-1-2-Name.dwg")]
    [InlineData("(5)-x-7-1-2-Name.dwg")]
    [InlineData("(5)-3-7-1-Name.dwg")]
    public void TryParse_returns_null_for_non_matching_names(string? fileName)
        => Assert.Null(ProjectFileNameParser.TryParse(fileName));
}
