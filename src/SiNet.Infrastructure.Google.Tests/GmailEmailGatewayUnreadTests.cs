using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailEmailGatewayUnreadTests
{
    [Theory]
    [InlineData(new[] { "INBOX", "UNREAD" }, true)]
    [InlineData(new[] { "INBOX" }, false)]
    [InlineData(new string[0], false)]
    public void ResolveIsUnread_reflects_gmail_unread_label(string[] labelIds, bool expected)
    {
        Assert.Equal(expected, GmailEmailGateway.ResolveIsUnread(labelIds));
    }

    [Fact]
    public void ResolveIsUnread_is_false_when_label_ids_missing()
    {
        Assert.False(GmailEmailGateway.ResolveIsUnread(null));
    }
}
