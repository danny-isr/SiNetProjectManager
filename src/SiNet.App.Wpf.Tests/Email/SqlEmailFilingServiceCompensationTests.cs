using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
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
        var sut = new SqlEmailFilingService(new StubDbContextFactory(options), gmail, NullLogger.Instance);

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
        var sut = new SqlEmailFilingService(new StubDbContextFactory(options), gmail, NullLogger.Instance);

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

    [Fact]
    public async Task FileToProject_duplicate_project_labels_does_not_create_and_asks_for_management()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Projects.Add(new Project
            {
                Id = 3070,
                Number = 3070,
                Title = "יבנה מזרח",
                NameAndNumber = "(3070)מגרש 166-יבנה מזרח",
            });
            await seed.SaveChangesAsync();
        }

        var gmail = new DuplicateConflictGmailModify();
        var sut = new SqlEmailFilingService(new StubDbContextFactory(options), gmail, NullLogger.Instance);

        var result = await sut.FileToProjectAsync(new FileEmailToProjectCommand(
            TargetProjectId: 3070,
            ActingUserId: 1,
            GmailMessageId: GmailMessageId));

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresLabelManagement);
        Assert.Contains("3070", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("ניהול התוויות", result.ErrorMessage, StringComparison.Ordinal);
        Assert.False(gmail.Created);
        Assert.Empty(gmail.Attached);
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

    private sealed class DuplicateConflictGmailModify : IEmailGmailModifyService
    {
        public bool Created { get; private set; }

        public List<string> Attached { get; } = [];

        public string RootLabel => EmailGmailLabelNames.RootLabel;

        public Task<string> GetOrCreateProjectLabelAsync(
            string location, string projectDisplayName, CancellationToken cancellationToken = default)
            => throw new GmailDuplicateProjectLabelException(3070, []);

        public Task<string> GetOrCreateProjectLabelAsync(
            string location, string projectDisplayName, int projectNumber, CancellationToken cancellationToken = default)
            => throw new GmailDuplicateProjectLabelException(projectNumber, []);

        public Task<string?> GetProjectLabelIdAsync(
            string location, string projectDisplayName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> GetProjectLabelIdByFullPathAsync(
            string fullPath, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task AttachProjectLabelAsync(
            string gmailMessageId, string projectLabelId, CancellationToken cancellationToken = default)
        {
            Attached.Add(projectLabelId);
            return Task.CompletedTask;
        }

        public Task RemoveProjectLabelAsync(
            string gmailMessageId, string projectLabelId, bool moveToInbox = true, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(
            string gmailMessageId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task RemoveProjectLabelsFromMessageAsync(
            string gmailMessageId,
            IReadOnlyList<string> labelIdsToRemove,
            bool moveToInbox = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ApplyTriageStatusLabelAsync(
            string gmailMessageId, EmailTriageStatus status, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsReadAsync(
            string gmailMessageId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RenameLabelAsync(
            string labelId, string newFullPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteLabelAsync(
            string labelId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
            string labelId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NullLogger : IAppLogger
    {
        public static NullLogger Instance { get; } = new();

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
