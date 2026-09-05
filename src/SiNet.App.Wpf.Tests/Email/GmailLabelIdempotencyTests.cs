using Google.Apis.Gmail.v1.Data;
using Xunit;
using SiNet.Infrastructure.Google;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailLabelIdempotencyTests
{
    [Fact]
    public void FindExactByName_returns_existing_label()
    {
        var labels = new[]
        {
            new Label { Id = "a", Name = "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי" },
        };

        var found = GmailLabelIdempotency.FindExactByName(
            labels,
            "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי");

        Assert.NotNull(found);
        Assert.Equal("a", found!.Id);
    }

    [Fact]
    public void FindExactByName_matches_gmail_collapsed_whitespace()
    {
        // DB NameAndNumber has a double space; Gmail persists a single space.
        var labels = new[]
        {
            new Label { Id = "Label_51", Name = "פרויקטים_משרד/אשקלון/(136)ניהול משרד - כללי" },
        };

        var found = GmailLabelIdempotency.FindExactByName(
            labels,
            "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי");

        Assert.NotNull(found);
        Assert.Equal("Label_51", found!.Id);
    }

    [Fact]
    public void ResolveIntendedAfterConflict_reuses_unique_match()
    {
        var labels = new[]
        {
            new Label { Id = "id-136", Name = "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי" },
            new Label { Id = "other", Name = "פרויקטים_משרד/אשקלון/(999)אחר" },
        };

        var resolved = GmailLabelIdempotency.ResolveIntendedAfterConflict(
            labels,
            "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי");

        Assert.Equal("id-136", resolved.Id);
    }

    [Fact]
    public void ResolveIntendedAfterConflict_reuses_whitespace_collapsed_unique_match()
    {
        var labels = new[]
        {
            new Label { Id = "Label_51", Name = "פרויקטים_משרד/אשקלון/(136)ניהול משרד - כללי" },
        };

        var resolved = GmailLabelIdempotency.ResolveIntendedAfterConflict(
            labels,
            "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי");

        Assert.Equal("Label_51", resolved.Id);
    }

    [Fact]
    public void ResolveIntendedAfterConflict_no_match_fails_with_near_candidates()
    {
        var labels = new[]
        {
            new Label { Id = "near", Name = "פרויקטים_משרד/אשקלון/(136)ניהול משרד XXX" },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GmailLabelIdempotency.ResolveIntendedAfterConflict(
                labels,
                "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי"));

        Assert.Contains("no exact match", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(136)ניהול משרד", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindExactByName_canonical_ambiguity_fails()
    {
        var labels = new[]
        {
            new Label { Id = "a", Name = "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי" },
            new Label { Id = "b", Name = "פרויקטים_משרד/אשקלון/(136)ניהול משרד - כללי" },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GmailLabelIdempotency.FindExactByName(
                labels,
                "פרויקטים_משרד/אשקלון/(136)ניהול  משרד - כללי"));

        Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveIntendedAfterConflict_ambiguous_fails()
    {
        var labels = new[]
        {
            new Label { Id = "1", Name = "OfficeSystem_Pending" },
            new Label { Id = "2", Name = "officesystem_pending" },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GmailLabelIdempotency.ResolveIntendedAfterConflict(labels, "OfficeSystem_Pending"));

        Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsLabelExistsOrConflicts_detects_message_and_inner()
    {
        Assert.True(GmailLabelIdempotency.IsLabelExistsOrConflicts(
            new InvalidOperationException("The service gmail has thrown an exception. HttpStatusCode is Conflict. Label name exists or conflicts")));

        Assert.True(GmailLabelIdempotency.IsLabelExistsOrConflicts(
            new Exception("outer", new Exception("Label name exists or conflicts"))));

        Assert.False(GmailLabelIdempotency.IsLabelExistsOrConflicts(
            new InvalidOperationException("quota exceeded")));
    }
}
