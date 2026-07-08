using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailListRowMapperThreadTests
{
    [Fact]
    public void ToEmailListRow_shows_thread_link_button_for_unfiled_thread_history()
    {
        var summary = CreateSummary(threadId: "thread-1");
        var threadStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["thread-1"] = new EmailProjectLinkInfo(
                IsLinked: true,
                ProjectId: 1042,
                ProjectNumber: "1042",
                ProjectName: "North",
                DisplayName: "1042 — North",
                ThreadProjectId: 1042,
                ThreadProjectName: "1042 — North",
                HasThreadHistory: true,
                GmailThreadId: "thread-1"),
        };

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(),
            threadStates,
            () => null);

        Assert.True(row.HasThreadHistory);
        Assert.True(row.ShowLinkToThreadButton);
        Assert.Equal(1042, row.ThreadProjectId);
        Assert.Contains("שייך לשרשור", row.ThreadLinkButtonText, StringComparison.Ordinal);
        Assert.Equal("#F5F5F5", row.RowBackgroundColor);
    }

    [Fact]
    public void ToEmailListRow_detects_mismatch_and_yellow_background()
    {
        var summary = CreateSummary(
            threadId: "thread-2",
            labelNames: [$"{EmailGmailLabelNames.RootLabel}/Tel Aviv/(2000) Other Project"]);
        var threadStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["thread-2"] = new EmailProjectLinkInfo(
                IsLinked: true,
                ProjectId: 1042,
                ProjectNumber: "1042",
                ProjectName: "North",
                DisplayName: "1042 — North",
                ThreadProjectId: 1042,
                ThreadProjectName: "1042 — North",
                HasThreadHistory: true,
                GmailThreadId: "thread-2"),
        };

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(),
            threadStates,
            () => null);

        Assert.True(row.IsProjectMismatch);
        Assert.True(row.ShowLinkToThreadButton);
        Assert.Contains("העבר לשרשור", row.ThreadLinkButtonText, StringComparison.Ordinal);
        Assert.Equal("#FFFFD54F", row.RowBackgroundColor);
    }

    private static EmailSummary CreateSummary(
        string threadId,
        IReadOnlyList<string>? labelNames = null) =>
        new(
            MessageId: "msg-1",
            ThreadId: threadId,
            From: new EmailAddress("sender@example.com"),
            Subject: "Subject",
            ReceivedAt: DateTimeOffset.UtcNow,
            AttachmentCount: 0,
            InternetMessageId: "<abc@mail.com>",
            To: new EmailAddress("me@example.com"),
            Snippet: "Snippet",
            LabelNames: labelNames ?? ["INBOX"]);
}
