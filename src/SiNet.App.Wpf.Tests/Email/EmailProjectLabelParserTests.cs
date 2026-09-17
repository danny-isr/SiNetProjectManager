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

    [Fact]
    public void TryParseProjectLabel_extracts_number_and_parent()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/יבנה/(3070)מגרש 166-יבנה מזרח";

        var entry = EmailProjectLabelParser.TryParseProjectLabel("label_xyz", path);

        Assert.NotNull(entry);
        Assert.Equal("label_xyz", entry!.LabelId);
        Assert.Equal(3070, entry.ProjectNumber);
        Assert.Equal("(3070)מגרש 166-יבנה מזרח", entry.ProjectDisplayName);
        Assert.Equal($"{EmailGmailLabelNames.RootLabel}/יבנה", entry.ParentPath);
    }

    [Fact]
    public void TryParseProjectLabel_rejects_parent_folder()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/יבנה";
        Assert.Null(EmailProjectLabelParser.TryParseProjectLabel("p", path));
    }

    [Fact]
    public void TryParseProjectLabel_rejects_number_outside_root()
    {
        Assert.Null(EmailProjectLabelParser.TryParseProjectLabel("o", "(3070)מגרש"));
    }

    [Fact]
    public void TryParseProjectLabel_rejects_digits_without_convention()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/יבנה/3070-מגרש";
        Assert.Null(EmailProjectLabelParser.TryParseProjectLabel("x", path));
    }
}
