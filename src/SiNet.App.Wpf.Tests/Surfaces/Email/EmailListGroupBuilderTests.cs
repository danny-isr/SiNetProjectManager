using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailListGroupBuilderTests
{
    [Fact]
    public void ToGroupHeaderDisplayName_for_project_label_returns_leaf_only()
    {
        var full = "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון";

        Assert.Equal("(1042)מגדלי הצפון", EmailListGroupBuilder.ToGroupHeaderDisplayName(full));
    }

    [Fact]
    public void ToGroupHeaderDisplayName_for_non_project_label_keeps_full_name()
    {
        Assert.Equal("Work", EmailListGroupBuilder.ToGroupHeaderDisplayName("Work"));
        Assert.Equal("OfficeSystem_Pending", EmailListGroupBuilder.ToGroupHeaderDisplayName("OfficeSystem_Pending"));
    }

    [Fact]
    public void Rebuild_uses_leaf_title_for_project_label_groups()
    {
        var fullPath = "פרויקטים_משרד/חיפה/(99)פרויקט בדיקה";
        var row = new EmailListRow(
            Id: "m1",
            Sender: "a@b.com",
            Subject: "s",
            Preview: "p",
            ReceivedOn: DateTime.UtcNow,
            GroupName: "INBOX",
            IsUnread: true,
            IsAssigned: true,
            AssignedProjectName: null,
            AttachmentCount: 0,
            PrimaryLabel: fullPath,
            LabelChipNames: [fullPath],
            IsFiledToProject: true);

        var labels = new[] { new GmailLabelInfo("L99", fullPath) };
        var created = new List<(string Id, string Name)>();

        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                [row],
                labels,
                ProjectGroup: null,
                ProjectLabelName: null,
                GroupByLabel: true,
                ExpandedByLabelId: new Dictionary<string, bool>()),
            (id, name) =>
            {
                created.Add((id, name));
                return new EmailLabelGroupViewModel(id, name, _ => Task.CompletedTask, _ => Task.CompletedTask);
            });

        Assert.Contains(created, c => c.Id == "L99" && c.Name == "(99)פרויקט בדיקה");
        Assert.Contains(result.DisplayGroups, g => g.LabelDisplayName == "(99)פרויקט בדיקה");
    }

    [Fact]
    public void ResolveExclusiveBucket_personal_wins_over_project_label()
    {
        var path = $"{EmailGmailLabelNames.RootLabel}/City/(1)P";
        var row = new EmailListRow(
            Id: "m1",
            Sender: "a@b.com",
            Subject: "s",
            Preview: "p",
            ReceivedOn: DateTime.UtcNow,
            GroupName: "INBOX",
            IsUnread: true,
            IsAssigned: true,
            AssignedProjectName: null,
            AttachmentCount: 0,
            LabelChipNames: [path, EmailGmailLabelNames.Personal],
            IsFiledToProject: true);

        var bucket = EmailListGroupBuilder.ResolveExclusiveBucket(row, path);
        Assert.Equal(EmailListGroupBuilder.ExclusiveBucketKind.Personal, bucket.Kind);
    }

    [Fact]
    public void Rebuild_orders_unfiled_before_irrelevant_before_personal()
    {
        var unfiled = CreateRow("u", ["INBOX"]);
        var irrelevant = CreateRow("i", [EmailGmailLabelNames.Irrelevant]);
        var personal = CreateRow("p", [EmailGmailLabelNames.Personal]);

        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                [personal, unfiled, irrelevant],
                [],
                ProjectGroup: null,
                ProjectLabelName: null,
                GroupByLabel: true,
                ExpandedByLabelId: new Dictionary<string, bool>()),
            (id, name) => new EmailLabelGroupViewModel(id, name, _ => Task.CompletedTask, _ => Task.CompletedTask));

        Assert.Equal(3, result.DisplayGroups.Count);
        Assert.Equal(EmailListGroupBuilder.UnfiledDisplayName, result.DisplayGroups[0].LabelDisplayName);
        Assert.Equal(EmailListGroupBuilder.IrrelevantDisplayName, result.DisplayGroups[1].LabelDisplayName);
        Assert.Equal(EmailListGroupBuilder.PersonalDisplayName, result.DisplayGroups[2].LabelDisplayName);
        Assert.DoesNotContain(
            result.DisplayGroups.SelectMany(g => g.Emails),
            r => result.DisplayGroups.Count(g => g.Emails.Any(e => e.Id == r.Id)) > 1);
    }

    [Fact]
    public void Rebuild_puts_multi_label_work_and_clients_in_single_unfiled_group()
    {
        var row = CreateRow("msg-multi", ["INBOX", "Work", "Clients"]);
        var labels = new[]
        {
            new GmailLabelInfo("Label_Work", "Work"),
            new GmailLabelInfo("Label_Clients", "Clients"),
        };

        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                [row],
                labels,
                ProjectGroup: null,
                ProjectLabelName: null,
                GroupByLabel: true,
                ExpandedByLabelId: new Dictionary<string, bool>()),
            (id, name) => new EmailLabelGroupViewModel(id, name, _ => Task.CompletedTask, _ => Task.CompletedTask));

        Assert.Single(result.DisplayGroups);
        Assert.Equal(EmailListGroupBuilder.UnfiledGroupId, result.DisplayGroups[0].LabelId);
        Assert.Contains(result.DisplayGroups[0].Emails, e => e.Id == "msg-multi");
    }

    private static EmailListRow CreateRow(string id, IReadOnlyList<string> labels) =>
        new(
            Id: id,
            Sender: "a@b.com",
            Subject: "s",
            Preview: "p",
            ReceivedOn: DateTime.UtcNow,
            GroupName: "INBOX",
            IsUnread: true,
            IsAssigned: false,
            AssignedProjectName: null,
            AttachmentCount: 0,
            PrimaryLabel: labels.FirstOrDefault(),
            LabelChipNames: labels);
}
