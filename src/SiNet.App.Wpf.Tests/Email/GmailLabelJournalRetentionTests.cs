using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailLabelJournalRetentionTests
{
    [Fact]
    public void Prune_drops_entries_older_than_30_days()
    {
        var now = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
        var keep = new GmailLabelJournalEntry(
            "L1",
            GmailLabelJournalAction.Renamed,
            "a/old",
            "a/new",
            1,
            now.AddDays(-10),
            GmailLabelJournalSource.AutoSync,
            []);
        var drop = new GmailLabelJournalEntry(
            "L2",
            GmailLabelJournalAction.Deleted,
            "b/gone",
            null,
            2,
            now.AddDays(-31),
            GmailLabelJournalSource.DuplicateResolve,
            ["m1", "m2"]);

        var pruned = GmailLabelJournalRetention.Prune([keep, drop], now);

        Assert.Single(pruned);
        Assert.Equal("L1", pruned[0].LabelId);
    }

    [Fact]
    public void SanitizeMailboxFileName_replaces_invalid_chars()
    {
        var name = GmailLabelJournalRetention.SanitizeMailboxFileName("User+Tag@Example.COM");
        Assert.Equal("user+tag@example.com", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }
}
