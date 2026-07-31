using SiNet.Infrastructure.Sql.Services.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class SqlQuoteSendProofStoreTests
{
    [Fact]
    public void ExtractGmailMessageId_reads_note_field()
    {
        var id = SqlQuoteSendProofStore.ExtractGmailMessageId(
            "GmailMessageId=abc123; Marker=SINET-QS-1-x");
        Assert.Equal("abc123", id);
    }

    [Fact]
    public void ExtractGmailMessageId_returns_null_when_missing()
    {
        Assert.Null(SqlQuoteSendProofStore.ExtractGmailMessageId("no proof here"));
        Assert.Null(SqlQuoteSendProofStore.ExtractGmailMessageId(null));
    }
}
