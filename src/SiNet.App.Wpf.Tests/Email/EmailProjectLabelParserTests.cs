using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailProjectLabelParserTests
{
    [Fact]
    public void TryParseProjectFromLabelPath_extracts_id_and_display_name()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/Tel Aviv/(1042) North Towers";

        var parsed = EmailProjectLabelParser.TryParseProjectFromLabelPath(path);

        Assert.NotNull(parsed);
        Assert.Equal(1042, parsed!.Value.ProjectId);
        Assert.Equal("(1042) North Towers", parsed.Value.ProjectDisplayName);
    }

    [Fact]
    public void TryParseProjectFromLabelPath_returns_null_for_non_project_label()
    {
        var parsed = EmailProjectLabelParser.TryParseProjectFromLabelPath("INBOX");
        Assert.Null(parsed);
    }
}
