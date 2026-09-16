using System.IO;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailLabelReadConsistencyTests
{
    private const string ProjectPath = "פרויקטים_משרד/תל אביב/(1042)מגדלי הצפון";

    [Fact]
    public void Dropped_project_label_names_map_as_unfiled()
    {
        var summary = Summary(["INBOX"]);

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            static () => null);

        Assert.False(row.IsFiledToProject);
        Assert.Null(row.FiledProjectLabelPath);
    }

    [Fact]
    public void Resolved_new_project_label_maps_filed_and_keeps_same_message_id()
    {
        var summary = Summary(["INBOX", ProjectPath], ProjectPath);

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            static () => null);

        Assert.Equal("msg-A", row.Id);
        Assert.True(row.IsFiledToProject);
        Assert.Equal(ProjectPath, row.FiledProjectLabelPath);
        Assert.Equal(1042, row.ProjectId);
    }

    [Fact]
    public void Same_message_stays_filed_after_full_group_rebuild()
    {
        var row = EmailListRowMapper.ToEmailListRow(
            Summary(["INBOX", ProjectPath], ProjectPath),
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            static () => null);

        var labels = new[] { new GmailLabelInfo("Label_X", ProjectPath) };
        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                [row],
                labels,
                ProjectGroup: null,
                ProjectLabelName: null,
                GroupByLabel: true,
                ExpandedByLabelId: new Dictionary<string, bool>()),
            static (id, name) => new EmailLabelGroupViewModel(id, name, _ => Task.CompletedTask, _ => Task.CompletedTask));

        Assert.True(row.IsFiledToProject);
        Assert.Contains(result.DisplayGroups, g => g.LabelId == "Label_X");
        Assert.Contains(result.DisplayGroups.SelectMany(g => g.Emails), e => e.Id == "msg-A" && e.IsFiledToProject);
        Assert.DoesNotContain(result.DisplayGroups, g => g.LabelId == EmailListGroupBuilder.UnfiledGroupId);
    }

    [Fact]
    public void Sql_thread_mapping_alone_does_not_file_a_different_message()
    {
        var summary = Summary(["INBOX"]) with
        {
            MessageId = "msg-B",
            ThreadId = "thr-1"
        };
        var threadStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["thr-1"] = new EmailProjectLinkInfo(
                IsLinked: true,
                ProjectId: 1042,
                ProjectNumber: "1042",
                ProjectName: "מגדלי הצפון",
                DisplayName: "1042 — מגדלי הצפון",
                ThreadProjectId: 1042,
                ThreadProjectName: "מגדלי הצפון",
                HasThreadHistory: true)
        };

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase),
            threadStates,
            static () => null);

        Assert.Equal("msg-B", row.Id);
        Assert.False(row.IsFiledToProject);
        Assert.Equal(1042, row.ThreadProjectId);
    }

    [Fact]
    public void Catalog_and_modify_share_the_same_di_registration()
    {
        var google = Read("src/SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs");
        var modify = Read("src/SiNet.Infrastructure.Google/GmailEmailModifyService.cs");
        var gateway = Read("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("AddSingleton<IGmailLabelCatalog, GmailLabelCatalog>", google, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IGmailLabelCatalog>()", google, StringComparison.Ordinal);
        Assert.Contains("_catalog.NotifyCreated", modify, StringComparison.Ordinal);
        Assert.Contains("_catalog.NotifyRenamed", modify, StringComparison.Ordinal);
        Assert.Contains("_catalog.NotifyDeleted", modify, StringComparison.Ordinal);
        Assert.Contains("ResolveForMessageAsync", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("_cachedLabelMap", gateway, StringComparison.Ordinal);
        Assert.Contains("RefreshAvailableLabelsAsync", Read("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilingCoordinator.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("signed-out", Read("src/SiNet.Infrastructure.Google/GmailClientProvider.cs"), StringComparison.Ordinal);
        Assert.Contains("SessionIdentity", Read("src/SiNet.Infrastructure.Google/GmailClientProvider.cs"), StringComparison.Ordinal);
        Assert.Contains("SessionKey => _provider.SessionIdentity", Read("src/SiNet.Infrastructure.Google/GmailApiLabelDirectory.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("LogoutAsync", Read("src/SiNet.Infrastructure.Google/GmailLabelCatalog.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("LogoutAsync", Read("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilingCoordinator.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("DisconnectGoogleOnMismatch: true", Read("src/SiNet.App.Wpf/Shell/NewShellViewModel.cs"), StringComparison.Ordinal);
    }

    private static EmailSummary Summary(IReadOnlyList<string> labelNames, string? primary = null) =>
        new(
            MessageId: "msg-A",
            ThreadId: "thr-1",
            From: new EmailAddress("a@b.com"),
            Subject: "Subj",
            ReceivedAt: DateTimeOffset.UtcNow,
            InternetMessageId: "<id@mail>",
            To: new EmailAddress("c@d.com"),
            Snippet: "snip",
            LabelNames: labelNames,
            PrimaryLabel: primary,
            IsUnread: false);

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return File.ReadAllText(Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
            dir = dir.Parent;
        }

        throw new InvalidOperationException("SiNet.sln not found.");
    }
}
