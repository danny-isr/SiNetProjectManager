using System.Net.Http;
using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNet.Application.Settings;
using SiNet.Infrastructure.AccBootstrap;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.AccBootstrap;
using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// L4W Gmail and ACC layers. Separate from <see cref="P0PilotLiveSmokeTests"/> because they are
/// separate opt-ins: the Pilot proofs must be runnable without touching Gmail or ACC at all.
/// <para>
/// Every ACC write goes through <see cref="PilotSmokeAccGuard"/>, whose allowlist starts empty and
/// only ever gains ids this run created or verified. See <c>docs/TEST_STRATEGY.md</c> ֲ§4W.
/// </para>
/// </summary>
[Collection(PilotSmokeTestCollection.Name)]
[Trait("Category", PilotSmokeFactAttribute.Category)]
public sealed class P0PilotGmailAccLiveSmokeTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    [PilotSmokeFact]
    public async Task Gmail_and_ACC_layers_write_only_to_disposable_targets()
    {
        var gate = PilotSmokeEnvironment.TryResolveSqlTier();
        Assert.True(gate.IsEnabled, gate.SkipReason);

        var gmailTier = PilotSmokeEnvironment.TryResolveGmailTier();
        if (!gmailTier.IsEnabled)
        {
            _output.WriteLine($"Gmail layer off: {gmailTier.SkipReason}");
            return;
        }

        var accTier = PilotSmokeEnvironment.TryResolveAccTier(gmailTier);
        var evidence = PilotSmokeEvidence.Create();
        evidence.Fact("Server", gate.ServerName);
        evidence.Fact("Database", gate.DatabaseName);
        evidence.Fact("Tier", "L4W Gmail" + (accTier.IsEnabled ? " + ACC" : " only (ACC off)"));

        var guard = new PilotSmokeAccGuard();
        await using var provider = PilotSmokeHost.Build(
            gate.ConnectionString!, accTier, guard, includeProcessBackbone: true);

        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var settings = provider.GetRequiredService<ISystemSettingsQueryService>();

        var login = await PilotSmokeSeed.EnsureOperatorLoginAsync(dbFactory, gate.OperatorUserId);
        evidence.Pass(
            "P2 Operator login resolves",
            $"Windows identity '{login.WindowsLogin}' resolves to SIUser {gate.OperatorUserId}"
            + (login.Changed
                ? $" after repointing LoginName from '{login.PreviousLoginName ?? "<empty>"}'."
                : " already."));

        var message = await ProbeGmailAsync(provider, gmailTier, evidence);
        var actingLogin = WindowsIdentity.GetCurrent().Name;
        evidence.Fact("Acting Windows login", actingLogin);

        InboxSettingSnapshot? inboxSnapshot = null;
        string? projectLabelPath = null;
        int? smokeProjectId = null;

        try
        {
            smokeProjectId = await CreateSmokeProjectAsync(dbFactory, gate.OperatorUserId, evidence);

            if (!accTier.IsEnabled)
            {
                evidence.Skipped(
                    "A1-A5 ACC layer",
                    $"ACC layer off: {accTier.SkipReason}");
                evidence.NotRun(
                    "G2 Gmail filing round-trip",
                    "Filing needs an EmailInboxMessage row, which only the real ACC ingest creates. "
                    + "Without the ACC layer there is no message row to file, and fabricating one "
                    + "would prove nothing about the production write order.");
                return;
            }

            await ProbeAccServiceAsync(provider, evidence);

            inboxSnapshot = await SwitchInboxTargetAsync(dbFactory, settings, accTier, evidence);

            var inbox = await BootstrapDisposableInboxAsync(provider, guard, accTier, evidence);

            var inboxMessageId = await IngestAsync(
                provider, dbFactory, message, actingLogin, inbox, evidence);

            projectLabelPath = await FileToProjectAsync(
                provider, dbFactory, message, inboxMessageId, smokeProjectId.Value, gate, evidence);

            var accProjectId = await EnsureProjectMappingAsync(
                provider, dbFactory, smokeProjectId.Value, guard, evidence);

            var tagged = await TagInboxAttachmentsForMoveAsync(
                provider, inboxMessageId, smokeProjectId.Value, gate, evidence);
            if (!tagged)
            {
                evidence.NotRun(
                    "A7 MoveToProject with AllFilesTransferred",
                    "No taggable inbox attachments (or no OutSidData catalog target). "
                    + "Send a message with at least one business attachment for a full MoveToProject proof.");
                return;
            }

            await MoveToProjectAsync(
                provider, inboxMessageId, smokeProjectId.Value, accProjectId, evidence);
        }
        finally
        {
            await UnfileAsync(provider, message, projectLabelPath, gate, evidence);

            if (inboxSnapshot is not null)
            {
                await RestoreInboxTargetAsync(dbFactory, inboxSnapshot, settings, evidence);
            }

            var blocked = guard.BlockedAttempts;
            evidence.Fact("ACC guard allowlist", string.Join(", ", guard.AllowedProjectIds));
            evidence.Fact(
                "ACC guard blocked attempts",
                blocked.Count == 0 ? "none" : string.Join(" | ", blocked));

            if (smokeProjectId is int created)
            {
                evidence.RequiresManualCleanup(
                    $"SQL project id {created} (title prefix '{PilotSmokeEnvironment.SmokeTitlePrefix}') "
                    + "and its rows — not deleted by the harness.");
            }

            evidence.Fact("Evidence file", evidence.MarkdownPath);
            _output.WriteLine($"Evidence: {evidence.MarkdownPath}");

            Assert.Empty(blocked);
        }
    }

    private sealed record GmailTestMessage(
        string MessageId,
        string ThreadId,
        string? InternetMessageId,
        string Subject,
        int AttachmentCount);

    private async Task<GmailTestMessage> ProbeGmailAsync(
        IServiceProvider provider,
        PilotSmokeEnvironment.GmailTier tier,
        PilotSmokeEvidence evidence)
    {
        var auth = provider.GetRequiredService<IConnectorAuthService>();
        var restored = await auth.TryRestoreSessionAsync();
        Assert.True(
            restored,
            "No stored Google token could be restored silently. Sign in interactively in the app "
            + "once, then re-run. This tier never opens a browser.");

        await auth.RefreshAccountProfileAsync();
        var connected = auth.ConnectedAccountEmail;
        evidence.Fact("Gmail authenticated mailbox", connected);

        Assert.True(
            string.Equals(connected?.Trim(), tier.ExpectedAccount, StringComparison.OrdinalIgnoreCase),
            $"The restored Google session authenticates as '{connected ?? "<unknown>"}' but "
            + $"{PilotSmokeEnvironment.GmailAccountEnv} declares '{tier.ExpectedAccount}'. "
            + "Refusing to write labels into an unexpected mailbox.");

        evidence.Pass(
            "G1 Gmail silent restore + mailbox identity",
            $"Restored without a browser and authenticated as the declared mailbox '{connected}'.");

        var gateway = provider.GetRequiredService<IEmailGateway>();
        var chosen = await PilotSmokeGmailMessagePicker.ResolveAsync(gateway, tier.SubjectToken);

        evidence.Pass(
            "G1b Test message located",
            $"messageId={chosen.MessageId} thread={chosen.ThreadId} "
            + $"attachments={chosen.AttachmentCount} selection={chosen.SelectionMode} "
            + $"subject='{TrimSubject(chosen.Subject)}'.");

        if (chosen.AttachmentCount == 0)
        {
            evidence.Skipped(
                "G1c Attachment present",
                "Gmail reports no attachments on the chosen message. Ingest may still create a "
                + "body PDF; MoveToProject needs a taggable inbox attachment row.");
        }
        else
        {
            evidence.Pass(
                "G1c Attachment present",
                $"Gmail reports {chosen.AttachmentCount} attachment(s) on the chosen message.");
        }

        return new GmailTestMessage(
            chosen.MessageId,
            chosen.ThreadId,
            chosen.InternetMessageId,
            chosen.Subject,
            chosen.AttachmentCount);
    }

    private static string TrimSubject(string subject) =>
        subject.Length <= 80 ? subject : subject[..77] + "...";

    private static async Task ProbeAccServiceAsync(IServiceProvider provider, PilotSmokeEvidence evidence)
    {
        var probe = provider.GetService<IAccServiceDiagnosticsProbe>();
        if (probe is null)
        {
            evidence.Skipped("A1 AccService diag", "IAccServiceDiagnosticsProbe is not registered.");
            return;
        }

        var mode = provider.GetService<IAccServiceModeProvider>();
        var result = await probe.ProbeAsync();
        evidence.Pass(
            "A1 AccService diag",
            $"mode={mode?.Mode.ToString() ?? "<unknown>"} reachable={result.Reachable} "
            + $"hasApiKey={result.HasApiKey} autodeskOk={result.AutodeskOk} dbOk={result.DbOk} "
            + $"autodesk='{result.AutodeskDetail}' db='{result.DbDetail}'. "
            + "Note: DbOk refers to AccService's own database, which is why the inbox bootstrap is "
            + "pinned to the local executor.");
    }

    private static async Task<int> CreateSmokeProjectAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int operatorUserId,
        PilotSmokeEvidence evidence)
    {
        var (placeId, placeCreated) = await PilotSmokeSeed.EnsureSiPlaceAsync(dbFactory, operatorUserId);
        evidence.Pass(
            "A2a SI place present",
            $"Place id={placeId} titled '{PilotSmokeEnvironment.RequiredAccPlaceTitle}' "
            + (placeCreated
                ? "was created by this run (docs/TEST_STRATEGY.md ֲ§4W.2.2)."
                : "already existed.")
            + " The ACC project name derives from it as \"SI-\" + Place.Title.");
        if (placeCreated)
        {
            evidence.RequiresManualCleanup(
                $"Place id {placeId} titled '{PilotSmokeEnvironment.RequiredAccPlaceTitle}' — created "
                + "by this run as an ACC guard precondition. Harmless to leave; it reappears on re-run.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();

        var companyId = await db.Companies.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        var contactId = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        var jobTypeId = await db.JobTypes.AsNoTracking().OrderBy(j => j.Id).Select(j => j.Id).FirstAsync();

        // Constructed directly: no IProjectAccMappingProvisioner, so creation itself cannot provision
        // ACC. The mapping is created later, explicitly, after the guard is armed.
        var creator = new SqlProjectCreateService(dbFactory);
        var result = await creator.CreateAsync(
            new CreateProjectCommand(
                Title: $"{PilotSmokeEnvironment.SmokeTitlePrefix} {DateTime.Now:MMdd-HHmm}",
                PlaceId: placeId,
                CompanyId: companyId,
                ContactId: contactId,
                JobTypeIds: [jobTypeId]),
            CancellationToken.None);

        Assert.True(result.Succeeded, $"Smoke project creation failed: {result.ErrorMessage}");

        evidence.Pass(
            "A2 Smoke project created",
            $"id={result.ProjectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}' "
            + "(no ACC provisioner in the construction).");
        evidence.Fact("Smoke project id", result.ProjectId!.Value.ToString());
        return result.ProjectId!.Value;
    }

    private sealed record InboxSettingSnapshot(
        string? InboxProjectName,
        bool InboxProjectNameRowExisted,
        AccSystemResource? OfficeInboxRow);

    /// <summary>
    /// Points the Office Inbox at the disposable project, and clears the cached
    /// <c>AccSystemResource</c> row so bootstrap re-resolves instead of reusing the previous project.
    /// </summary>
    private static async Task<InboxSettingSnapshot> SwitchInboxTargetAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        ISystemSettingsQueryService settings,
        PilotSmokeEnvironment.AccTier accTier,
        PilotSmokeEvidence evidence)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var nameRow = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.SettingKey == SystemSettingKeys.InboxProjectName);
        var officeInbox = await db.AccSystemResources
            .FirstOrDefaultAsync(r => r.Key == AccConstants.OfficeInboxResourceKey);

        var snapshot = new InboxSettingSnapshot(
            nameRow?.SettingValue,
            nameRow is not null,
            officeInbox is null
                ? null
                : new AccSystemResource
                {
                    Key = officeInbox.Key,
                    AccHubId = officeInbox.AccHubId,
                    AccProjectId = officeInbox.AccProjectId,
                    AccRootFolderId = officeInbox.AccRootFolderId,
                    AccInboxFolderId = officeInbox.AccInboxFolderId,
                    Notes = officeInbox.Notes,
                    CreatedAtUtc = officeInbox.CreatedAtUtc,
                    UpdatedAtUtc = officeInbox.UpdatedAtUtc,
                });

        evidence.Fact("InboxProjectName before", snapshot.InboxProjectName ?? "<absent>");
        evidence.Fact(
            "AccSystemResource OfficeInbox before",
            snapshot.OfficeInboxRow is null
                ? "<absent>"
                : $"accProject={snapshot.OfficeInboxRow.AccProjectId} "
                  + $"inboxFolder={snapshot.OfficeInboxRow.AccInboxFolderId}");

        if (nameRow is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = SystemSettingKeys.InboxProjectName,
                SettingValue = accTier.InboxProjectName!,
                LastUpdated = DateTime.UtcNow,
            });
        }
        else
        {
            nameRow.SettingValue = accTier.InboxProjectName!;
            nameRow.LastUpdated = DateTime.UtcNow;
        }

        if (officeInbox is not null)
        {
            db.AccSystemResources.Remove(officeInbox);
        }

        await db.SaveChangesAsync();

        var effective = await settings.GetSystemSettingsAsync();
        Assert.Equal(accTier.InboxProjectName, effective.EmailOffice.InboxProjectName?.Trim());

        evidence.Pass(
            "A3 Inbox target switched",
            $"InboxProjectName='{accTier.InboxProjectName}' confirmed through the real settings "
            + "reader; cached OfficeInbox resource row cleared so bootstrap re-resolves. Both are "
            + "restored in finally.");
        return snapshot;
    }

    private static async Task RestoreInboxTargetAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        InboxSettingSnapshot snapshot,
        ISystemSettingsQueryService settings,
        PilotSmokeEvidence evidence)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var nameRow = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.SettingKey == SystemSettingKeys.InboxProjectName);

        if (!snapshot.InboxProjectNameRowExisted)
        {
            if (nameRow is not null)
            {
                db.SystemSettings.Remove(nameRow);
            }
        }
        else if (nameRow is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = SystemSettingKeys.InboxProjectName,
                SettingValue = snapshot.InboxProjectName!,
                LastUpdated = DateTime.UtcNow,
            });
        }
        else
        {
            nameRow.SettingValue = snapshot.InboxProjectName!;
            nameRow.LastUpdated = DateTime.UtcNow;
        }

        var current = await db.AccSystemResources
            .FirstOrDefaultAsync(r => r.Key == AccConstants.OfficeInboxResourceKey);
        if (current is not null)
        {
            db.AccSystemResources.Remove(current);
            await db.SaveChangesAsync();
        }

        if (snapshot.OfficeInboxRow is not null)
        {
            db.AccSystemResources.Add(snapshot.OfficeInboxRow);
        }

        await db.SaveChangesAsync();

        var effective = await settings.GetSystemSettingsAsync();
        evidence.Fact("InboxProjectName after restore", effective.EmailOffice.InboxProjectName ?? "<absent>");
        evidence.Pass(
            "A3r Inbox target restored",
            "InboxProjectName and the OfficeInbox resource row are back to their pre-run values.");
    }

    private static async Task<AccInboxBootstrapResult> BootstrapDisposableInboxAsync(
        IServiceProvider provider,
        PilotSmokeAccGuard guard,
        PilotSmokeEnvironment.AccTier accTier,
        PilotSmokeEvidence evidence)
    {
        var bootstrap = provider.GetRequiredService<IAccInboxBootstrapService>();
        var result = await bootstrap.EnsureAsync();

        guard.Allow(result.AccProjectId, $"disposable smoke inbox '{accTier.InboxProjectName}'");

        evidence.Pass(
            "A4 Disposable inbox project ensured",
            $"hub={result.HubId} accProject={result.AccProjectId} root={result.AccRootFolderId} "
            + $"inboxFolder={result.AccInboxFolderId}. Added to the ACC guard allowlist.");
        evidence.RequiresManualCleanup(
            $"ACC project '{accTier.InboxProjectName}' (id {result.AccProjectId}) and everything "
            + "uploaded into it. Delete from the ACC Admin Console — the application only soft-deletes.");
        return result;
    }

    private static async Task<int> IngestAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        GmailTestMessage message,
        string actingLogin,
        AccInboxBootstrapResult inbox,
        PilotSmokeEvidence evidence)
    {
        var executor = provider.GetRequiredService<IEmailAccIngestionExecutor>();
        var result = await executor.IngestToInboxAsync(
            new EmailAccUploadCommand(
                message.MessageId,
                message.ThreadId,
                message.InternetMessageId,
                actingLogin,
                AllowZeroAttachmentIngest: true));

        Assert.True(
            result.Succeeded,
            $"ACC ingest failed ({result.Outcome}): {result.ErrorMessage}");
        Assert.NotNull(result.InboxMessageId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.EmailInboxMessages
            .AsNoTracking()
            .FirstAsync(m => m.Id == result.InboxMessageId!.Value);

        Assert.Equal(inbox.AccProjectId, row.InboxAccProjectId);

        evidence.Pass(
            "A5 Real ingest into the disposable inbox",
            $"outcome={result.Outcome} inboxMessageId={row.Id} "
            + $"attachments={result.AttachmentsUploaded}/{result.TotalAttachments} "
            + $"accProject={row.InboxAccProjectId} accFolder={row.InboxAccFolderId} "
            + $"internetMessageId={row.InternetMessageId}. The row was created by the production "
            + "ingest path from real Gmail data, not fabricated.");
        evidence.Fact("EmailInboxMessage id", row.Id.ToString());
        return row.Id;
    }

    private static async Task<string?> FileToProjectAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        GmailTestMessage message,
        int inboxMessageId,
        int projectId,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var filing = provider.GetRequiredService<IEmailFilingService>();
        var result = await filing.FileToProjectAsync(
            new FileEmailToProjectCommand(
                TargetProjectId: projectId,
                ActingUserId: gate.OperatorUserId,
                GmailMessageId: message.MessageId,
                InboxMessageId: inboxMessageId,
                GmailThreadId: message.ThreadId,
                InternetMessageId: message.InternetMessageId));

        Assert.True(result.Succeeded, $"Gmail filing failed: {result.ErrorMessage}");
        Assert.Equal(projectId, result.AssignedProjectId);

        var modify = provider.GetRequiredService<IEmailGmailModifyService>();
        var labelIds = await modify.GetProjectLabelIdsOnMessageAsync(message.MessageId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var mirroredProjectId = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.Id == inboxMessageId)
            .Select(m => m.ProjectId)
            .FirstAsync();
        Assert.Equal(projectId, mirroredProjectId);

        var projectTitle = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Title)
            .FirstAsync();

        evidence.Pass(
            "G2 Gmail filing round-trip (file)",
            $"Real Gmail label write succeeded; the message now carries {labelIds.Count} project "
            + $"label(s) under root '{modify.RootLabel}', and the SQL mirror points at project "
            + $"{mirroredProjectId}. Gmail-first write order held (docs/EMAIL_ACC_SOURCE_OF_TRUTH.md).");

        return projectTitle;
    }

    private static async Task<string> EnsureProjectMappingAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        PilotSmokeAccGuard guard,
        PilotSmokeEvidence evidence)
    {
        var provisioner = provider.GetRequiredService<IProjectAccMappingProvisioner>();
        await provisioner.EnsureMappingAsync(projectId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var mapping = await db.ProjectAccMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId);

        Assert.NotNull(mapping);
        Assert.False(
            string.IsNullOrWhiteSpace(mapping!.AccProjectId),
            "EnsureMappingAsync produced no AccProjectId.");

        var expectedName = "SI-" + PilotSmokeEnvironment.RequiredAccPlaceTitle;
        Assert.Equal(expectedName, mapping.AccProjectName?.Trim());

        guard.Allow(mapping.AccProjectId!, $"project ACC mapping for smoke project {projectId}");

        evidence.Pass(
            "A6 Project ACC mapping provisioned",
            $"accProject={mapping.AccProjectId} name='{mapping.AccProjectName}' "
            + $"targetFolder={mapping.AccTargetFolderId}. Name equals '{expectedName}' as derived "
            + "from Place.Title, confirming a development target. Added to the guard allowlist.");
        evidence.RequiresManualCleanup(
            $"ACC project '{mapping.AccProjectName}' (id {mapping.AccProjectId}) — delete only the "
            + "files this run uploaded, and the project itself only if this run created it.");

        return mapping.AccProjectId!;
    }

    /// <summary>
    /// MoveToProject files only attachments tagged with an OutSidData catalog slot. The UI does this
    /// before «העבר לפרויקט»; the smoke must do the same through the production tagging service.
    /// </summary>
    private static async Task<bool> TagInboxAttachmentsForMoveAsync(
        IServiceProvider provider,
        int inboxMessageId,
        int projectId,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var tagging = provider.GetRequiredService<IEmailAttachmentTaggingService>();
        var attachments = await tagging.LoadInboxAttachmentsAsync(inboxMessageId);
        var taggable = attachments.Where(a => a.IsTaggable).ToList();

        if (taggable.Count == 0)
        {
            evidence.Skipped(
                "A6b Tag inbox attachments",
                "No taggable attachments on the inbox row — MoveToProject has nothing to file.");
            return false;
        }

        var targets = await tagging.LoadTagTargetsAsync(projectId);
        var target = targets.FirstOrDefault();
        if (target is null)
        {
            evidence.Fail(
                "A6b Tag inbox attachments",
                "No OutSidData catalog slots exist in the database — cannot tag for MoveToProject.");
            return false;
        }

        var alternatives = await tagging.LoadAlternativesAsync(projectId);
        var alternativeId = EmailProjectAlternativeOption.ResolveDefaultId(alternatives);

        var tagged = 0;
        foreach (var att in taggable)
        {
            var result = await tagging.SetTagAsync(
                new EmailAttachmentTagCommand(
                    att.InboxAttachmentId,
                    target.ProjectFileId,
                    alternativeId,
                    gate.OperatorUserId));

            Assert.True(
                result.Succeeded,
                $"Tagging attachment {att.InboxAttachmentId} '{att.FileName}' failed: {result.ErrorMessage}");
            tagged++;
        }

        evidence.Pass(
            "A6b Tag inbox attachments",
            $"Tagged {tagged}/{taggable.Count} attachment(s) to catalog slot "
            + $"'{target.DisplayName}' (projectFileId={target.ProjectFileId}, alt={alternativeId?.ToString() ?? "null"}).");
        return true;
    }

    private static async Task MoveToProjectAsync(
        IServiceProvider provider,
        int inboxMessageId,
        int projectId,
        string accProjectId,
        PilotSmokeEvidence evidence)
    {
        var move = provider.GetRequiredService<IEmailMoveToProjectService>();
        if (!move.IsAvailable)
        {
            evidence.Skipped("A7 MoveToProject", "IEmailMoveToProjectService reports IsAvailable=false.");
            return;
        }

        var result = await move.MoveAsync(
            new EmailMoveToProjectDetailCommand(inboxMessageId, projectId, TaskId: null, TaskResultCode: null));

        var failures = result.AttachmentFailures is null
            ? "none"
            : string.Join(" | ", result.AttachmentFailures.Select(f => f.ToString()));

        Assert.True(
            result.Succeeded,
            $"MoveToProject failed: {result.Message}. Attachment failures: {failures}");
        Assert.True(
            result.AllFilesTransferred,
            $"Not every tagged file transferred: moved={result.MovedCount} failed={result.FailedCount} "
            + $"total={result.TotalCount} alreadySameSource={result.AlreadySameSourceCount}. "
            + $"Failures: {failures}");

        evidence.Pass(
            "A7 MoveToProject with AllFilesTransferred",
            $"accProject={accProjectId} moved={result.MovedCount} alreadySameSource="
            + $"{result.AlreadySameSourceCount} total={result.TotalCount} failed={result.FailedCount}. "
            + $"Message: {result.Message}");
    }

    private static async Task UnfileAsync(
        IServiceProvider provider,
        GmailTestMessage message,
        string? projectLabelPath,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        if (projectLabelPath is null)
        {
            evidence.NotRun("G3 Gmail unfile", "No project label was attached, so nothing to reverse.");
            return;
        }

        try
        {
            var filing = provider.GetRequiredService<IEmailFilingService>();
            var result = await filing.UnfileFromProjectAsync(
                new UnfileEmailCommand(
                    ActingUserId: gate.OperatorUserId,
                    GmailMessageId: message.MessageId,
                    GmailThreadId: message.ThreadId,
                    InternetMessageId: message.InternetMessageId));

            if (result.Succeeded)
            {
                var modify = provider.GetRequiredService<IEmailGmailModifyService>();
                var remaining = await modify.GetProjectLabelIdsOnMessageAsync(message.MessageId);
                evidence.Pass(
                    "G3 Gmail unfile",
                    $"Project filing removed; {remaining.Count} project label(s) remain on the "
                    + "message. The mailbox is back to its pre-run state.");
            }
            else
            {
                evidence.Fail(
                    "G3 Gmail unfile",
                    $"{result.ErrorMessage}. Remove the project label from the test message manually.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            evidence.Fail(
                "G3 Gmail unfile",
                $"{ex.GetType().Name}: {ex.Message}. Remove the project label manually.");
        }
    }
}
