using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Infrastructure.Sql.Services.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

/// <summary>
/// Guards the #6 reliability fix: when the SQL sync fails after the Gmail project label was
/// applied, <see cref="SqlEmailFilingService.FileToProjectAsync"/> must compensate by removing the
/// just-attached label so Gmail and SQL do not drift into an orphaned-label state.
/// </summary>
public sealed class SqlEmailFilingServiceCompensationTests
{
    private const string GmailMessageId = "gmail-msg-1";

    [Fact]
    public async Task FileToProject_when_sql_sync_fails_removes_the_attached_label()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.Add(new Project
            {
                Id = 500,
                Number = 500,
                Title = "North",
                NameAndNumber = "500 — North",
            });
            seed.EmailInboxMessages.Add(new EmailInboxMessage
            {
                Id = 7,
                MessageUniqueId = "unique-7",
                ThreadUniqueId = "thread-unique-7",
                GmailThreadId = "gmail-thread-7",
            });
            await seed.SaveChangesAsync();
        }

        var gmail = new RecordingGmailModifyService();
        var sut = new SqlEmailFilingService(new StubDbContextFactory(options), gmail);

        // InboxMessageId is set, so the sync reaches ExecuteUpdateAsync, which the InMemory provider
        // does not support and throws — this is the "DB failure after Gmail label applied" case.
        var command = new FileEmailToProjectCommand(
            TargetProjectId: 500,
            ActingUserId: 1,
            GmailMessageId: GmailMessageId,
            InboxMessageId: 7);

        var result = await sut.FileToProjectAsync(command);

        Assert.False(result.Succeeded);
        Assert.Single(gmail.AttachedLabelIds);
        Assert.Contains((GmailMessageId, gmail.AttachedLabelIds[0]), gmail.RemovedLabels);
    }

    [Fact]
    public async Task FileToProject_when_sql_sync_succeeds_does_not_compensate()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.Add(new Project
            {
                Id = 501,
                Number = 501,
                Title = "South",
                NameAndNumber = "501 — South",
            });
            await seed.SaveChangesAsync();
        }

        var gmail = new RecordingGmailModifyService();
        var sut = new SqlEmailFilingService(new StubDbContextFactory(options), gmail);

        // No matching inbox row (no InboxMessageId, unknown message id) -> the sync resolves a null
        // inbox row and returns without touching ExecuteUpdateAsync, so filing succeeds cleanly.
        var command = new FileEmailToProjectCommand(
            TargetProjectId: 501,
            ActingUserId: 1,
            GmailMessageId: GmailMessageId);

        var result = await sut.FileToProjectAsync(command);

        Assert.True(result.Succeeded);
        Assert.Equal(501, result.AssignedProjectId);
        Assert.Single(gmail.AttachedLabelIds);
        Assert.Empty(gmail.RemovedLabels);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingGmailModifyService : IEmailGmailModifyService
    {
        public List<string> AttachedLabelIds { get; } = new();

        public List<(string MessageId, string LabelId)> RemovedLabels { get; } = new();

        public string RootLabel => "SiNet";

        public Task<string> GetOrCreateProjectLabelAsync(
            string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            Task.FromResult("label-created");

        public Task<string?> GetProjectLabelIdAsync(
            string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("label-created");

        public Task<string?> GetProjectLabelIdByFullPathAsync(
            string fullPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task AttachProjectLabelAsync(
            string gmailMessageId, string projectLabelId, CancellationToken cancellationToken = default)
        {
            AttachedLabelIds.Add(projectLabelId);
            return Task.CompletedTask;
        }

        public Task RemoveProjectLabelAsync(
            string gmailMessageId, string projectLabelId, bool moveToInbox = true, CancellationToken cancellationToken = default)
        {
            RemovedLabels.Add((gmailMessageId, projectLabelId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(
            string gmailMessageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task RemoveProjectLabelsFromMessageAsync(
            string gmailMessageId,
            IReadOnlyList<string> labelIdsToRemove,
            bool moveToInbox = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyTriageStatusLabelAsync(
            string gmailMessageId, EmailTriageStatus status, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAsReadAsync(
            string gmailMessageId, CancellationToken cancellationToken = default)
        {
            MarkedAsReadMessageIds.Add(gmailMessageId);
            return Task.CompletedTask;
        }

        public Task RenameLabelAsync(
            string labelId, string newFullPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteLabelAsync(
            string labelId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
            string labelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public List<string> MarkedAsReadMessageIds { get; } = new();
    }
}
