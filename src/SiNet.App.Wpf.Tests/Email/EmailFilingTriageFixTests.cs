using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Domain.ValueObjects;
using SiNet.Infrastructure.Sql.Services.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class EmailFilingTriageFixTests
{
    [Fact]
    public async Task Cross_mailbox_thread_unique_id_resolves_one_project_without_duplicate_mapping()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.Add(new Project
            {
                Id = 2786,
                Number = 2786,
                Title = "Tower",
                NameAndNumber = "2786 — Tower",
            });
            await seed.SaveChangesAsync();
        }

        var factory = new StubDbContextFactory(options);
        var filing = new SqlEmailFilingService(factory, new NoopGmailModify(), new NullLogger());
        var first = await filing.FileToProjectAsync(new FileEmailToProjectCommand(
            2786,
            1,
            "msg-a",
            GmailThreadId: "AAA",
            InternetMessageId: "<root@example.com>",
            ThreadUniqueId: "GLOBAL-123"));
        Assert.True(first.Succeeded);

        var second = await filing.FileToProjectAsync(new FileEmailToProjectCommand(
            2786,
            1,
            "msg-b",
            GmailThreadId: "BBB",
            InternetMessageId: "<reply@example.com>",
            ThreadUniqueId: "GLOBAL-123"));
        Assert.True(second.Succeeded);

        var query = new SqlEmailThreadLinkQueryService(factory);
        var byUnique = await query.GetLinkStatesByThreadUniqueIdsAsync(["GLOBAL-123"]);
        Assert.True(byUnique.TryGetValue("GLOBAL-123", out var info));
        Assert.Equal(2786, info.ThreadProjectId);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.ThreadStatusMappings.CountAsync());
        Assert.Equal("GLOBAL-123", db.ThreadStatusMappings.Single().ThreadUniqueId);
    }

    [Fact]
    public async Task Reassignment_updates_same_mapping_row()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.AddRange(
                new Project { Id = 2786, Number = 2786, Title = "A", NameAndNumber = "2786 — A" },
                new Project { Id = 3001, Number = 3001, Title = "B", NameAndNumber = "3001 — B" });
            await seed.SaveChangesAsync();
        }

        var factory = new StubDbContextFactory(options);
        var filing = new SqlEmailFilingService(factory, new NoopGmailModify(), new NullLogger());
        await filing.FileToProjectAsync(new FileEmailToProjectCommand(
            2786, 1, "msg-a", GmailThreadId: "AAA", ThreadUniqueId: "GLOBAL-123"));
        await filing.FileToProjectAsync(new FileEmailToProjectCommand(
            3001, 1, "msg-a", GmailThreadId: "AAA", ThreadUniqueId: "GLOBAL-123"));

        await using var db = factory.CreateDbContext();
        var mapping = Assert.Single(db.ThreadStatusMappings);
        Assert.Equal(3001, mapping.ProjectId);
        Assert.Equal("GLOBAL-123", mapping.ThreadUniqueId);
    }

    [Fact]
    public void Mapper_uses_global_thread_unique_id_not_gmail_thread_id()
    {
        var summary = new EmailSummary(
            "msg-b",
            "BBB",
            new EmailAddress("b@x.com"),
            "hello",
            DateTimeOffset.UtcNow,
            InternetMessageId: "<reply@example.com>",
            References: "<GLOBAL-123@example.com>");
        var uniqueStates = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.Ordinal)
        {
            ["GLOBAL-123@example.com"] = new EmailProjectLinkInfo(
                true,
                2786,
                "2786",
                "Tower",
                "2786 — Tower",
                ThreadUniqueId: "GLOBAL-123@example.com",
                ThreadProjectId: 2786,
                ThreadProjectName: "2786 — Tower",
                HasThreadHistory: true),
        };

        var row = EmailListRowMapper.ToEmailListRow(
            summary,
            new Dictionary<string, EmailProjectLinkInfo>(),
            new Dictionary<string, EmailProjectLinkInfo>(),
            () => null,
            uniqueStates);

        Assert.Equal("GLOBAL-123@example.com", row.ThreadUniqueId);
        Assert.Equal(2786, row.ThreadProjectId);
        Assert.True(row.ShowLinkToThreadButton);
        Assert.False(row.IsFiledToProject);
    }

    [Fact]
    public void Subject_query_cleans_prefixes_and_keeps_full_subject_when_no_match()
    {
        Assert.Equal("Tower weekly", EmailFilingSubjectQuery.CleanSubject("RE: FW: Tower weekly"));
        var query = EmailFilingSubjectQuery.BuildSearchText(
            "RE: unknown words only",
            _ => 0);
        Assert.Equal("unknown words only", query);
    }

    [Fact]
    public void Subject_query_narrows_until_one_or_last_non_empty()
    {
        var query = EmailFilingSubjectQuery.BuildSearchText(
            "RE: Tower North weekly",
            text => text switch
            {
                "Tower" => 4,
                "Tower North" => 1,
                _ => 0,
            });
        Assert.Equal("Tower North", query);
    }

    [Fact]
    public void Filing_user_messages_hide_raw_db_exceptions()
    {
        var message = EmailFilingUserMessages.FromException(new InvalidOperationException("DbUpdateException See inner exception"));
        Assert.Equal(EmailFilingUserMessages.SqlFailed, message);
        Assert.DoesNotContain("DbUpdateException", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewer_subject_is_copyable_readonly_textbox()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "Detail", "EmailViewerPaneView.xaml"));
        Assert.Contains("Email.Viewer.Subject", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("העתק כותרת", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"{Binding Subject}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enqueue_marks_row_busy_without_waiting_for_gmail()
    {
        var filing = new Surfaces.Email.EmailListViewModelTestFixtures.DelayingFilingService();
        var project = Surfaces.Email.EmailListViewModelTestFixtures.CreateProject();
        var sut = new EmailListViewModel(
            new Surfaces.Email.EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubAuthService(),
            filing,
            statusService: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubCurrentProjectContext(project),
            new Surfaces.Email.EmailListViewModelTestFixtures.StubCurrentUser(7));
        var row = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false)
            with { ThreadUniqueId = "GLOBAL-123" };
        sut.ReplaceRowsForTests([row]);

        sut.EnqueueFileToProject(row, project);
        sut.EnqueueFileToProject(row, project);

        Assert.True(sut.IsRowActionBusy(row.Id));
        Assert.Contains("פעולה כבר רצה", sut.LoadWarning ?? string.Empty, StringComparison.Ordinal);
        await Task.Delay(80);
        Assert.Equal(1, filing.FileCallCount);
        filing.Release();
    }

    [Fact]
    public async Task Multiple_rows_can_queue_without_blocking_the_second_enqueue()
    {
        var filing = new Surfaces.Email.EmailListViewModelTestFixtures.DelayingFilingService();
        var project = Surfaces.Email.EmailListViewModelTestFixtures.CreateProject();
        var sut = new EmailListViewModel(
            new Surfaces.Email.EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubAuthService(),
            filing,
            statusService: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubCurrentProjectContext(project),
            new Surfaces.Email.EmailListViewModelTestFixtures.StubCurrentUser(7));
        var rowA = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false)
            with { Id = "msg-a" };
        var rowB = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false)
            with { Id = "msg-b" };
        sut.ReplaceRowsForTests([rowA, rowB]);

        sut.EnqueueFileToProject(rowA, project);
        sut.EnqueueFileToProject(rowB, project);

        Assert.True(sut.IsRowActionBusy(rowA.Id));
        Assert.True(sut.IsRowActionBusy(rowB.Id));
        await Task.Delay(80);
        Assert.Equal(1, filing.FileCallCount);
        filing.Release();
    }

    [Fact]
    public async Task Visible_row_patch_for_a_does_not_overwrite_selected_b_header()
    {
        var project = Surfaces.Email.EmailListViewModelTestFixtures.CreateProject();
        var list = new EmailListViewModel(
            new Surfaces.Email.EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubAuthService());
        var detail = new EmailDetailViewModel(
            list,
            new Surfaces.Email.EmailListViewModelTestFixtures.PagingEmailGateway(),
            new Surfaces.Email.EmailListViewModelTestFixtures.StubCurrentProjectContext(project));
        var rowA = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false)
            with { Id = "msg-a", Subject = "Subject A", Sender = "a@example.com" };
        var rowB = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false)
            with { Id = "msg-b", Subject = "Subject B", Sender = "b@example.com" };

        await detail.ApplySelectionAsync(rowA);
        Assert.Equal("Subject A", detail.Viewer.Subject);
        await detail.ApplySelectionAsync(rowB);
        Assert.Equal("Subject B", detail.Viewer.Subject);
        Assert.Equal("b@example.com", detail.Viewer.Sender);

        detail.ApplyVisibleRowPatch(rowA with
        {
            Subject = "Subject A filed",
            IsFiledToProject = true,
            Sender = "a-filed@example.com",
        });

        Assert.Equal("Subject B", detail.Viewer.Subject);
        Assert.Equal("b@example.com", detail.Viewer.Sender);
    }

    [Fact]
    public void Acc_failure_keeps_successful_filing_and_allows_retry()
    {
        var filed = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: 12, isFiledToProject: true);
        var failed = EmailAccSelectionHandler.CreateAccPostProcessFailedRow(filed);

        Assert.True(failed.IsFiledToProject);
        Assert.Equal(EmailFilingUserMessages.AccFailed, failed.ActionErrorText);
        Assert.Equal(EmailFilingUserMessages.AccFailed, failed.AccStatusDisplay);
        Assert.Equal(EmailAccProcessingStatus.Failed, failed.AccProcessingStatus);
        Assert.True(failed.CanRetryFiling);
        Assert.DoesNotContain("DbUpdateException", failed.ActionErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_query_stops_at_one_result_without_selecting_a_project()
    {
        var query = EmailFilingSubjectQuery.BuildSearchText(
            "RE: UniqueTower weekly",
            text => text == "UniqueTower" ? 1 : 0);
        Assert.Equal("UniqueTower", query);

        var host = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "WpfEmailFilingProjectPickerHost.cs"));
        Assert.Contains("selector.SearchText", host, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectProject(", host, StringComparison.Ordinal);
        Assert.Contains("InMemoryCurrentProjectContext", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Filing_picker_receives_subject_and_retry_is_wired()
    {
        var detail = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailDetailViewModel.cs"));
        var card = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Email", "EmailListItemCard.xaml"));
        Assert.Contains("PickProjectAsync(_selectedEmail.Subject)", detail, StringComparison.Ordinal);
        Assert.Contains("RetryBackgroundWorkCommand", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Filing_to_other_project_creates_visible_group()
    {
        var sut = new EmailListViewModel(
            new Surfaces.Email.EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new Surfaces.Email.EmailListViewModelTestFixtures.StubAuthService());
        var row = Surfaces.Email.EmailListViewModelTestFixtures.CreateRow(inboxMessageId: null, isFiledToProject: false);
        sut.ReplaceRowsForTests([row]);
        sut.SetGroupByLabel(true);

        var filed = EmailListRowMapper.BuildOptimisticFiledRow(
            row,
            new SiNet.Application.Projects.ProjectSummaryDto(2786, "2786", "Tower", "Tel Aviv", null, null, null, null, true));
        sut.ApplyLocalEmailMutationForTests(filed);

        Assert.Contains(
            sut.DisplayGroups,
            group => group.Emails.Any(item => item.Id == row.Id)
                     && !string.Equals(group.LabelId, EmailListGroupBuilder.UnfiledGroupId, StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class NoopGmailModify : IEmailGmailModifyService
    {
        public string RootLabel => "SiNet";

        public Task<string> GetOrCreateProjectLabelAsync(string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            Task.FromResult("label-1");

        public Task<string?> GetProjectLabelIdAsync(string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("label-1");

        public Task<string?> GetProjectLabelIdByFullPathAsync(string fullPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task AttachProjectLabelAsync(string gmailMessageId, string projectLabelId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveProjectLabelAsync(string gmailMessageId, string projectLabelId, bool moveToInbox = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(string gmailMessageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RemoveProjectLabelsFromMessageAsync(
            string gmailMessageId,
            IReadOnlyList<string> labelIdsToRemove,
            bool moveToInbox = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyTriageStatusLabelAsync(string gmailMessageId, EmailTriageStatus status, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAsReadAsync(string gmailMessageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RenameLabelAsync(string labelId, string newFullPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteLabelAsync(string labelId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(string labelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NullLogger : SiNet.Application.Abstractions.Logging.IAppLogger
    {
        public static readonly NullLogger Instance = new();

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
