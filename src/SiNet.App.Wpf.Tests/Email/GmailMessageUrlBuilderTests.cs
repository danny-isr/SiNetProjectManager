using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailMessageUrlBuilderTests
{
    [Fact]
    public void Build_opens_the_message_for_the_first_browser_account_when_email_unknown()
    {
        var url = GmailMessageUrlBuilder.Build("18f0abc123def");

        Assert.Equal("https://mail.google.com/mail/u/0/#all/18f0abc123def", url);
    }

    [Fact]
    public void Build_pins_the_connected_google_account_when_known()
    {
        var url = GmailMessageUrlBuilder.Build("18f0abc123def", "ops@shia.co.il");

        Assert.Equal(
            "https://mail.google.com/mail/u/ops%40shia.co.il/#all/18f0abc123def",
            url);
    }

    [Fact]
    public void Build_rejects_a_missing_message_id()
    {
        Assert.Throws<ArgumentException>(() => GmailMessageUrlBuilder.Build(" "));
    }
}
