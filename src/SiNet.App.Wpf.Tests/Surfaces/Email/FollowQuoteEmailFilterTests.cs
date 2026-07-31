using System.IO;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Email.Internal;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class FollowQuoteEmailFilterTests
{
    [Fact]
    public void ApplyClientRowFilters_keeps_only_follow_quote_thread()
    {
        var list = new EmailListViewModel();
        list.FollowQuoteThreadFilter = "thread-abc";

        var rows = new[]
        {
            CreateRow("m1", "thread-abc"),
            CreateRow("m2", "thread-other"),
            CreateRow("m3", "thread-abc"),
        };

        var filtered = ((IEmailListRowMutator)list).ApplyClientRowFilters(rows);

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.Equal("thread-abc", r.ThreadId));
    }

    [Fact]
    public void EmailOpenHints_are_optional_on_work_surface_context()
    {
        var hints = new EmailOpenHints(
            GmailThreadId: "t1",
            AfterGmailMessageId: "msg1",
            CounterpartAddress: "a@b.com",
            OfferProjectWorkFallback: true);

        var context = new WorkSurfaceContext(
            TaskId: 7,
            ProjectId: 3142,
            WorkflowInstanceId: 1,
            ComponentKey: WorkSurfaceComponentKeys.EmailFiling,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: ["QuoteApprovedByClient"],
            EmailHints: hints);

        Assert.Equal("t1", context.EmailHints!.GmailThreadId);
        Assert.True(context.EmailHints.OfferProjectWorkFallback);
    }

    [Fact]
    public void WorkSurfaceLauncher_routes_follow_quote_email_first()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "SiNet.App.Wpf",
                "WorkSurfaces",
                "WorkSurfaceLauncher.cs"));

        Assert.Contains("OpenFollowQuoteEmailAsync", source, StringComparison.Ordinal);
        Assert.Contains("IEmailSurfaceHost", source, StringComparison.Ordinal);
        Assert.Contains("FollowQuoteApproval", source, StringComparison.Ordinal);
        Assert.Contains("OfferProjectWorkFallback: true", source, StringComparison.Ordinal);
    }

    private static EmailListRow CreateRow(string id, string threadId) =>
        new(
            Id: id,
            Sender: "a@b.com",
            Subject: "s",
            Preview: string.Empty,
            ReceivedOn: DateTime.UtcNow,
            GroupName: "g",
            IsUnread: false,
            IsAssigned: false,
            AssignedProjectName: null,
            AttachmentCount: 1,
            ThreadId: threadId,
            HasThreadHistory: true);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
