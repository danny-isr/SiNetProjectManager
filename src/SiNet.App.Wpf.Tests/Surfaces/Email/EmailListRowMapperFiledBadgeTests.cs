using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailListRowMapperFiledBadgeTests
{
    [Fact]
    public void Sql_project_id_alone_does_not_show_linked_badge()
    {
        var summary = CreateSummary(labelNames: ["INBOX", "UNREAD"]);
        var messageStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["abc@mail.com"] = new EmailProjectLinkInfo(
                IsLinked: true,
                ProjectId: 130,
                ProjectNumber: "130",
                ProjectName: "SQL only",
                DisplayName: "130 — SQL only",
                InboxMessageId: 1,
                InboxProjectId: 130),
        };

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            messageStates,
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            static () => null);

        Assert.False(row.IsFiledToProject);
        Assert.False(row.IsLinked);
        Assert.Equal(EmailProjectLinkState.Unlinked, row.ProjectLinkState);
        Assert.Equal("לא משויך", row.ProjectLinkDisplay);
        Assert.Contains("קישור במסד", row.ProjectDiagnosticsTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Gmail_project_label_shows_linked_badge_with_leaf_name()
    {
        const string path = "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון";
        var summary = CreateSummary(labelNames: ["INBOX", path], primaryLabel: path);

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            static () => null);

        Assert.True(row.IsFiledToProject);
        Assert.True(row.IsLinked);
        Assert.Equal("משויך", row.ProjectLinkDisplay);
        Assert.Contains("1042", row.LinkedProjectBadge, StringComparison.Ordinal);
        Assert.Contains("תווית Gmail", row.ProjectDiagnosticsTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterDisplayLabels_orders_project_then_other_then_office_system()
    {
        var ordered = EmailListRowMapper.FilterDisplayLabels(
        [
            "INBOX",
            EmailGmailLabelNames.Personal,
            "CustomTag",
            "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון",
            EmailGmailLabelNames.Pending,
            "UNREAD",
        ]);

        Assert.Equal(
            [
                "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון",
                "CustomTag",
                EmailGmailLabelNames.Pending,
                EmailGmailLabelNames.Personal,
            ],
            ordered);
    }

    private static EmailSummary CreateSummary(
        IReadOnlyList<string> labelNames,
        string? primaryLabel = null) =>
        new(
            MessageId: "msg-1",
            ThreadId: "thr-1",
            From: new EmailAddress("a@b.com"),
            Subject: "Subj",
            ReceivedAt: DateTimeOffset.UtcNow,
            InternetMessageId: "abc@mail.com",
            To: new EmailAddress("c@d.com"),
            Snippet: "snip",
            LabelNames: labelNames,
            PrimaryLabel: primaryLabel,
            IsUnread: false);
}
