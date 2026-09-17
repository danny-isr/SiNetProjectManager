using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailProjectLabelIdentityTests
{
    private const string Root = EmailGmailLabelNames.RootLabel;

    [Fact]
    public void Canonical_path_reuses_existing_label()
    {
        var index = Index(
            ("label_a", $"{Root}/יבנה/(3070)מגרש 166-יבנה מזרח"));

        var decision = GmailProjectLabelResolver.Resolve(index, 3070);

        Assert.Equal(GmailProjectLabelResolveKind.ReuseExisting, decision.Kind);
        Assert.Equal("label_a", decision.Existing!.LabelId);
    }

    [Fact]
    public void Same_number_under_other_folder_reuses_and_does_not_create()
    {
        var index = Index(
            ("label_old", $"{Root}/ישן/(3070)מגרש 166-יבנה מזרח"));

        var decision = GmailProjectLabelResolver.Resolve(index, 3070);

        Assert.Equal(GmailProjectLabelResolveKind.ReuseExisting, decision.Kind);
        Assert.Equal("label_old", decision.Existing!.LabelId);
        Assert.Contains("/ישן/", decision.Existing.FullPath, StringComparison.Ordinal);
        Assert.DoesNotContain("/יבנה/", decision.Existing.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_display_name_reuses_same_number()
    {
        var index = Index(
            ("label_1", $"{Root}/יבנה/(1844)שם ישן"));

        var decision = GmailProjectLabelResolver.Resolve(index, 1844);

        Assert.Equal(GmailProjectLabelResolveKind.ReuseExisting, decision.Kind);
        Assert.Equal("label_1", decision.Existing!.LabelId);
    }

    [Fact]
    public void No_match_creates_canonical()
    {
        var index = Index(("other", $"{Root}/יבנה/(1000)אחר"));

        var decision = GmailProjectLabelResolver.Resolve(index, 3070);

        Assert.Equal(GmailProjectLabelResolveKind.CreateCanonical, decision.Kind);
        Assert.Empty(decision.Matches);
    }

    [Fact]
    public void Two_labels_same_number_is_duplicate_conflict()
    {
        var index = Index(
            ("a", $"{Root}/ישן/(3070)ישן"),
            ("b", $"{Root}/יבנה/(3070)חדש"));

        var decision = GmailProjectLabelResolver.Resolve(index, 3070);

        Assert.Equal(GmailProjectLabelResolveKind.DuplicateConflict, decision.Kind);
        Assert.Equal(2, decision.Matches.Count);
        Assert.Null(decision.Existing);
    }

    [Fact]
    public void Same_number_outside_root_is_ignored()
    {
        var index = Index(
            ("personal", "אישי/(3070)פרטי"),
            ("inboxish", "(3070)בלי שורש"));

        var decision = GmailProjectLabelResolver.Resolve(index, 3070);

        Assert.Equal(GmailProjectLabelResolveKind.CreateCanonical, decision.Kind);
    }

    [Fact]
    public void Parent_folder_is_not_a_project_label()
    {
        var index = Index(("folder", $"{Root}/יבנה"));

        Assert.Equal(GmailProjectLabelResolveKind.CreateCanonical, GmailProjectLabelResolver.Resolve(index, 3070).Kind);
    }

    [Fact]
    public void Session_key_is_retained_on_index()
    {
        var first = GmailProjectLabelIndex.Build(
            [("a", $"{Root}/יבנה/(1)A")],
            Root,
            "acct-1",
            catalogGeneration: 3);
        var second = GmailProjectLabelIndex.Build(
            [("b", $"{Root}/יבנה/(2)B")],
            Root,
            "acct-2",
            catalogGeneration: 4);

        Assert.Equal("acct-1", first.SessionKey);
        Assert.Equal("acct-2", second.SessionKey);
        Assert.Empty(second.Find(1));
        Assert.Single(second.Find(2));
    }

    private static GmailProjectLabelIndex Index(params (string Id, string Name)[] labels)
        => GmailProjectLabelIndex.Build(labels, Root, "session", 1);
}
