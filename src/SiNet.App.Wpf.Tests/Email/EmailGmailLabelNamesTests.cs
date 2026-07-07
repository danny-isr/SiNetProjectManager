using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailGmailLabelNamesTests
{
    [Theory]
    [InlineData("פרויקטים_משרד/תל אביב/(3026)ניסיון", true)]
    [InlineData("פרויקטים_משרד/General", false)]
    [InlineData("OfficeSystem_Pending", false)]
    public void IsProjectLabel_requires_root_location_and_project(string label, bool expected) =>
        Assert.Equal(expected, EmailGmailLabelNames.IsProjectLabel(label));

    [Fact]
    public void FindProjectLabelPath_returns_first_project_label()
    {
        var labels = new[]
        {
            "INBOX",
            "OfficeSystem_Pending",
            "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון",
        };

        var path = EmailGmailLabelNames.FindProjectLabelPath(labels);

        Assert.Equal("פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון", path);
    }
}
