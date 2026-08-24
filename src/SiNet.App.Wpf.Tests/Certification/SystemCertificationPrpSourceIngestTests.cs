using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Domain.ValueObjects;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationPrpSourceIngestTests
{
    private const string GmailMessageId = "gmail-cert-source-1";
    private const string InternetMessageId = "<sys-cert-source@test.local>";
    private const string AttachmentName = "quote.pdf";
    private const string AccProjectId = "acc-inbox-project-test";
    private const string AccFolderId = "acc-inbox-folder-test";
    private const string ContentSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task AlreadyIngested_skips_production_ingest()
    {
        var harness = await CreateHarnessAsync(seedInboxId: 10);

        var inbox = await SystemCertificationPrpSourceIngest.TryEnsureFullyIngestedAsync(
            harness.Provider,
            harness.DbFactory,
            harness.Context,
            proposalDefinitionId: 1,
            harness.GmailDetails,
            harness.Evidence,
            CancellationToken.None);

        Assert.NotNull(inbox);
        Assert.Equal(10, inbox!.InboxMessageId);
        Assert.Equal(0, harness.IngestExecutor.CallCount);
        AssertStepPassed(harness.Evidence, "cert.prp.source_ingest");
        Assert.Contains("skipped", GetStepDetail(harness.Evidence, "cert.prp.source_ingest"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotIngested_runs_production_ingest_then_proceeds()
    {
        var harness = await CreateHarnessAsync(seedInboxId: null);

        var inbox = await SystemCertificationPrpSourceIngest.TryEnsureFullyIngestedAsync(
            harness.Provider,
            harness.DbFactory,
            harness.Context,
            proposalDefinitionId: 1,
            harness.GmailDetails,
            harness.Evidence,
            CancellationToken.None);

        Assert.NotNull(inbox);
        Assert.Equal(1, harness.IngestExecutor.CallCount);
        Assert.True(inbox!.InboxMessageId > 0);
        AssertStepPassed(harness.Evidence, "cert.prp.source_ingest");
        AssertStepPassed(harness.Evidence, "cert.prp.source_ingest_sql_readback");
        AssertStepPassed(harness.Evidence, "cert.prp.source_ingest_acc_readback");
    }

    [Fact]
    public async Task IngestFailure_does_not_create_proposal_instance()
    {
        var harness = await CreateHarnessAsync(seedInboxId: null);
        harness.IngestExecutor.ShouldFail = true;

        var instanceId = await TryResolveAndMaybeStartAsync(harness);

        Assert.Equal(0, instanceId);
        Assert.Equal(0, harness.ExecutionService.CallCount);
        AssertStepFailed(harness.Evidence, "cert.prp.source_ingest");
        await AssertProposalInstanceCountAsync(harness.DbFactory, 0);
    }

    [Fact]
    public async Task AccInboxReadBackFailure_does_not_create_proposal_instance()
    {
        var harness = await CreateHarnessAsync(seedInboxId: 20);
        harness.FolderBrowser.ReturnEmptyItems = true;

        var instanceId = await TryResolveAndMaybeStartAsync(harness);

        Assert.Equal(0, instanceId);
        Assert.Equal(0, harness.ExecutionService.CallCount);
        AssertStepFailed(harness.Evidence, "cert.prp.source_ingest_acc_readback");
        await AssertProposalInstanceCountAsync(harness.DbFactory, 0);
    }

    [Fact]
    public void SqlAttachmentsMatchGmailIdentity_requires_every_gmail_filename_in_sql()
    {
        var gmail = CreateGmailDetails();
        var matching = new[]
        {
            new SystemCertificationPrpSourceIngest.SqlAttachmentSnapshot(1, AttachmentName, "item-1"),
        };

        Assert.True(SystemCertificationPrpSourceIngest.SqlAttachmentsMatchGmailIdentity(gmail, matching));
        Assert.False(SystemCertificationPrpSourceIngest.SqlAttachmentsMatchGmailIdentity(
            gmail,
            [new SystemCertificationPrpSourceIngest.SqlAttachmentSnapshot(1, "other.pdf", "item-1")]));
    }

    private static async Task<int> TryResolveAndMaybeStartAsync(Harness harness)
    {
        var inbox = await SystemCertificationPrpSourceIngest.TryEnsureFullyIngestedAsync(
            harness.Provider,
            harness.DbFactory,
            harness.Context,
            proposalDefinitionId: 1,
            harness.GmailDetails,
            harness.Evidence,
            CancellationToken.None);
        if (inbox is null)
        {
            return 0;
        }

        return await SystemCertificationPrpCorridorSupport.ExecuteCreatePriceQuoteAsync(
            harness.Provider,
            harness.DbFactory,
            inbox,
            proposalDefinitionId: 1,
            operatorUserId: 1,
            harness.Evidence,
            CancellationToken.None);
    }

    private static async Task<Harness> CreateHarnessAsync(int? seedInboxId)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var dbFactory = new InMemoryDbFactory(options);
        await SeedDatabaseAsync(dbFactory, seedInboxId);

        var evidence = SystemCertificationEvidence.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.prp.source_ingest", "Production ACC inbox ingest before CreatePriceQuote"),
            ("cert.prp.source_ingest_sql_readback", "SQL inbox row and attachments match source Gmail"),
            ("cert.prp.source_ingest_acc_readback", "ACC Inbox folder read-back proves ingested files"),
            ("cert.prp.inbox", "Fully ingested inbox row resolved for CreatePriceQuote"),
            ("cert.prp.create_price_quote", "Start PRP through IEmailSuggestedActionExecutionService"));
        var target = new SystemCertificationEnvironment.Target(
            IsEnabled: true,
            SkipReason: null,
            Violation: null,
            ConnectionString: "Server=.;Database=SystemCertificationTest;Trusted_Connection=True;",
            DeclaredDataSource: ".",
            DeclaredDatabase: "SystemCertificationTest",
            ActualServerName: ".",
            ActualDatabaseName: "SystemCertificationTest",
            WindowsIdentityName: Environment.UserName,
            OperatorUserId: 1);

        var ingestExecutor = new RecordingIngestExecutor(dbFactory, AccProjectId, AccFolderId, AttachmentName);
        var folderBrowser = new FakeAccFolderBrowser(AccProjectId, AccFolderId, AttachmentName);
        var executionService = new CountingExecutionService();
        var taggingService = new FakeTaggingService(AttachmentName);

        var context = new SystemCertificationHost.SystemCertificationRunContext(
            target,
            OperatorUserId: 1,
            new SystemCertificationEnvironment.GmailLayer(true, null, null, "test@example.com"),
            new SystemCertificationEnvironment.AccLayer(true, null, null, "SI", "SYS-CERT-INBOX"),
            AccGuard: null);

        var services = new ServiceCollection();
        services.AddSingleton(dbFactory);
        services.AddSingleton<IEmailAccIngestionExecutor>(ingestExecutor);
        services.AddSingleton<IAccInboxBootstrapService>(new FakeAccInboxBootstrap(AccProjectId, AccFolderId));
        services.AddSingleton<IAccFolderBrowserService>(folderBrowser);
        services.AddSingleton<IEmailAttachmentTaggingService>(taggingService);
        services.AddSingleton<IEmailSuggestedActionExecutionService>(executionService);

        return new Harness(
            services.BuildServiceProvider(),
            dbFactory,
            context,
            evidence,
            CreateGmailDetails(),
            ingestExecutor,
            folderBrowser,
            executionService);
    }

    private static async Task SeedDatabaseAsync(InMemoryDbFactory dbFactory, int? seedInboxId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Projects.Add(new Project { Id = 1, Title = "[SYS-CERT] seed" });
        db.Siusers.Add(new Siuser { Id = 1, LoginName = Environment.UserName });

        if (seedInboxId is int inboxId)
        {
            var unique = EmailMessageIdentity.GetMessageUniqueId(InternetMessageId, GmailMessageId);
            var threadUnique = EmailMessageIdentity.GetThreadUniqueId(null, null, InternetMessageId);
            db.EmailInboxMessages.Add(new EmailInboxMessage
            {
                Id = inboxId,
                ProjectId = 1,
                MessageUniqueId = unique,
                InternetMessageId = InternetMessageId,
                GmailThreadId = "thread-1",
                ThreadUniqueId = threadUnique,
                ThreadKey = EmailMessageIdentity.GetThreadKey(threadUnique)[..8],
                Subject = "[SYS-CERT] source",
                InboxAccProjectId = AccProjectId,
                InboxAccFolderId = AccFolderId,
            });
            db.EmailInboxAttachments.Add(new EmailInboxAttachment
            {
                Id = inboxId * 10,
                MessageId = inboxId,
                AttachmentIndex = 0,
                OriginalFileName = AttachmentName,
                SavedFileName = AttachmentName,
                ContentSha256 = ContentSha256,
                AccItemId = "acc-item-1",
            });
        }

        await db.SaveChangesAsync();
    }

    private static EmailMessageDetails CreateGmailDetails() =>
        new(
            GmailMessageId,
            "thread-1",
            new EmailAddress("sender@example.com"),
            "[SYS-CERT] source subject",
            DateTimeOffset.UtcNow,
            BodyText: "body",
            Attachments:
            [
                new EmailMessageAttachmentDetails("att-1", AttachmentName, "application/pdf", 100),
            ],
            InternetMessageId: InternetMessageId);

    private static async Task AssertProposalInstanceCountAsync(
        InMemoryDbFactory dbFactory,
        int expectedCount)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var count = await db.WorkflowInstances.CountAsync();
        Assert.Equal(expectedCount, count);
    }

    private static void AssertStepPassed(SystemCertificationEvidence evidence, string stepId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetJsonPath(evidence)));
        var step = document.RootElement.GetProperty("Steps").EnumerateArray()
            .Single(e => e.GetProperty("Name").GetString() == stepId);
        Assert.Equal((int)CertificationResult.Pass, step.GetProperty("Result").GetInt32());
    }

    private static void AssertStepFailed(SystemCertificationEvidence evidence, string stepId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetJsonPath(evidence)));
        var step = document.RootElement.GetProperty("Steps").EnumerateArray()
            .Single(e => e.GetProperty("Name").GetString() == stepId);
        Assert.Equal((int)CertificationResult.Fail, step.GetProperty("Result").GetInt32());
    }

    private static string GetStepDetail(SystemCertificationEvidence evidence, string stepId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetJsonPath(evidence)));
        var step = document.RootElement.GetProperty("Steps").EnumerateArray()
            .Single(e => e.GetProperty("Name").GetString() == stepId);
        return step.GetProperty("Detail").GetString() ?? string.Empty;
    }

    private static string GetJsonPath(SystemCertificationEvidence evidence) =>
        evidence.MarkdownPath.Replace(".md", ".json", StringComparison.Ordinal);

    private sealed record Harness(
        IServiceProvider Provider,
        InMemoryDbFactory DbFactory,
        SystemCertificationHost.SystemCertificationRunContext Context,
        SystemCertificationEvidence Evidence,
        EmailMessageDetails GmailDetails,
        RecordingIngestExecutor IngestExecutor,
        FakeAccFolderBrowser FolderBrowser,
        CountingExecutionService ExecutionService);

    private sealed class InMemoryDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }

    private sealed class RecordingIngestExecutor(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string accProjectId,
        string accFolderId,
        string attachmentName) : IEmailAccIngestionExecutor
    {
        public int CallCount { get; private set; }

        public bool ShouldFail { get; set; }

        public async Task<EmailAccUploadResult> IngestToInboxAsync(
            EmailAccUploadCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ShouldFail)
            {
                return new EmailAccUploadResult(
                    EmailAccUploadOutcome.Failed,
                    null,
                    null,
                    0,
                    1,
                    "simulated ingest failure",
                    1);
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var unique = EmailMessageIdentity.GetMessageUniqueId(command.InternetMessageId, command.GmailMessageId);
            var threadUnique = EmailMessageIdentity.GetThreadUniqueId(null, null, command.InternetMessageId!);
            var inbox = new EmailInboxMessage
            {
                ProjectId = 1,
                MessageUniqueId = unique,
                InternetMessageId = command.InternetMessageId!,
                GmailThreadId = command.GmailThreadId,
                ThreadUniqueId = threadUnique,
                ThreadKey = EmailMessageIdentity.GetThreadKey(threadUnique)[..8],
                Subject = "[SYS-CERT] ingested",
                InboxAccProjectId = accProjectId,
                InboxAccFolderId = accFolderId,
            };
            db.EmailInboxMessages.Add(inbox);
            await db.SaveChangesAsync(cancellationToken);

            db.EmailInboxAttachments.Add(new EmailInboxAttachment
            {
                MessageId = inbox.Id,
                AttachmentIndex = 0,
                OriginalFileName = attachmentName,
                SavedFileName = attachmentName,
                ContentSha256 = ContentSha256,
                AccItemId = "acc-item-ingest",
            });
            await db.SaveChangesAsync(cancellationToken);

            return new EmailAccUploadResult(
                EmailAccUploadOutcome.Succeeded,
                unique,
                inbox.Id,
                1,
                1,
                null,
                1);
        }
    }

    private sealed class FakeAccInboxBootstrap(string accProjectId, string accFolderId) : IAccInboxBootstrapService
    {
        public Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccInboxBootstrapResult(
                "hub-test",
                accProjectId,
                "root-folder",
                accFolderId));
    }

    private sealed class FakeAccFolderBrowser(
        string accProjectId,
        string accFolderId,
        string attachmentName) : IAccFolderBrowserService
    {
        public bool ReturnEmptyItems { get; set; }

        public Task<AccFolderBrowseResult?> BrowseAsync(
            string projectId,
            string? folderId = null,
            CancellationToken cancellationToken = default)
        {
            _ = projectId;
            if (ReturnEmptyItems)
            {
                return Task.FromResult<AccFolderBrowseResult?>(
                    new AccFolderBrowseResult(accProjectId, folderId ?? accFolderId, []));
            }

            return Task.FromResult<AccFolderBrowseResult?>(
                new AccFolderBrowseResult(
                    accProjectId,
                    folderId ?? accFolderId,
                    [
                        new AccFolderBrowseEntry(
                            "acc-item-1",
                            attachmentName,
                            AccFolderEntryKind.Item,
                            FileSize: 100,
                            LastModifiedTime: DateTime.UtcNow,
                            CreateTime: DateTime.UtcNow),
                    ]));
        }
    }

    private sealed class FakeTaggingService(string attachmentName) : IEmailAttachmentTaggingService
    {
        public Task<IReadOnlyList<EmailInboxAttachmentTagState>> LoadInboxAttachmentsAsync(
            int inboxMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailInboxAttachmentTagState>>(
            [
                new EmailInboxAttachmentTagState(
                    inboxMessageId * 10,
                    attachmentName,
                    AttachmentIndex: 0,
                    ProjectFileId: null,
                    ProjectFileTitle: null,
                    ProjectAlternativeId: null,
                    IsTaggable: true,
                    AccItemId: "acc-item-1"),
            ]);

        public Task<IReadOnlyList<EmailProjectAlternativeOption>> LoadAlternativesAsync(
            int projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailProjectAlternativeOption>>([]);

        public Task<EmailProjectAlternativeOption?> CreateAlternativeAsync(
            int projectId,
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailProjectAlternativeOption?>(null);

        public Task<IReadOnlyList<EmailAttachmentTagTarget>> LoadTagTargetsAsync(
            int projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailAttachmentTagTarget>>([]);

        public Task<EmailAttachmentTagPickerCatalog> LoadTagPickerCatalogAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailAttachmentTagPickerCatalog([], [], []));

        public Task<EmailAttachmentTagValidationResult> ValidateTagAsync(
            EmailAttachmentTagValidationQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailAttachmentTagValidationResult(true, null, false));

        public Task<EmailAttachmentTagResult> SetTagAsync(
            EmailAttachmentTagCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailAttachmentTagResult(true, null));
    }

    private sealed class CountingExecutionService : IEmailSuggestedActionExecutionService
    {
        public int CallCount { get; private set; }

        public Task<EmailSuggestedActionExecutionResult> ExecuteAsync(
            EmailSuggestedActionExecutionCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EmailSuggestedActionExecutionResult(
                Succeeded: true,
                RequiresFollowUp: false,
                Message: "simulated",
                WorkflowInstanceId: 99,
                InboxMessageId: command.InboxMessageId));
        }
    }
}
