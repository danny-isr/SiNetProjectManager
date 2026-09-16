using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class GmailAccountProfileRefreshTests
{
    [Fact]
    public void Temporary_profile_failure_keeps_known_email_while_signed_in()
    {
        var next = GmailAccountProfileRefresh.AfterLookup(
            isSignedIn: true,
            previousEmail: "danny@si-eng.co.il",
            profileEmail: null,
            lookupFailed: true);

        Assert.Equal("danny@si-eng.co.il", next);
    }

    [Fact]
    public void Signed_out_session_clears_displayed_email()
    {
        var next = GmailAccountProfileRefresh.AfterLookup(
            isSignedIn: false,
            previousEmail: "danny@si-eng.co.il",
            profileEmail: null,
            lookupFailed: true);

        Assert.Null(next);
    }

    [Fact]
    public void Successful_profile_read_updates_email()
    {
        var next = GmailAccountProfileRefresh.AfterLookup(
            isSignedIn: true,
            previousEmail: "old@si-eng.co.il",
            profileEmail: "  shirly@si-eng.co.il  ",
            lookupFailed: false);

        Assert.Equal("shirly@si-eng.co.il", next);
    }

    [Fact]
    public void Empty_profile_email_does_not_erase_known_account_while_signed_in()
    {
        var next = GmailAccountProfileRefresh.AfterLookup(
            isSignedIn: true,
            previousEmail: "danny@si-eng.co.il",
            profileEmail: "  ",
            lookupFailed: false);

        Assert.Equal("danny@si-eng.co.il", next);
    }
}
