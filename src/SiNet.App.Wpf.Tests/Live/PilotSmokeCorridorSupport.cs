using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Detail;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// PRP corridor helpers: email-driven <c>CreatePriceQuote</c> start and production-seam task
/// completion for surfaces that do not expose a unique <see cref="ITaskNavigationService"/> event.
/// </summary>
internal static class PilotSmokeCorridorSupport
{
    internal sealed record CorridorInbox(
        int? InboxMessageId,
        EmailGmailSourceIdentity? GmailSource,
        string SelectionDetail);

    internal static void RegisterCorridorServices(
        IServiceCollection services,
        PilotSmokeEnvironment.GmailTier? gmailTier)
    {
        services.AddSiNetEmailDetailSql();
        services.AddTransient<IOpenQuoteProjectDecisionService, OpenQuoteProjectDecisionService>();

        if (gmailTier is { IsEnabled: true })
        {
            // Mirror PilotSmokeHost: vault-backed client secrets + the same token folder as the WPF app.
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection().Build());
            services.AddSiNetSecrets();
            services.AddSiNetEmailReadSql();
            services.AddSiNetEmailWriteSql();
            services.AddSiNetGoogle(static options =>
            {
                options.ApplicationName = "SiNet.PilotSmoke.Corridor";
                options.AllowInteractiveSignIn = false;
                options.TokenStorePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SiNet",
                    "google-token");
            });
        }
    }

    internal static async Task<CorridorInbox?> TryResolveInboxForCreatePriceQuoteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        PilotSmokeEnvironment.GmailTier gmailTier,
        PilotSmokeEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        if (!gmailTier.IsEnabled)
        {
            evidence.Skipped(
                "S3b Corridor inbox",
                $"{PilotSmokeEnvironment.GmailEnabledEnv} is off — PRP corridor uses manual Start only.");
            return null;
        }

        var auth = provider.GetRequiredService<IConnectorAuthService>();
        var restored = await auth.TryRestoreSessionAsync(cancellationToken);
        if (!restored)
        {
            evidence.Fail(
                "S3b Corridor inbox",
                "Gmail token could not be restored silently — CreatePriceQuote cannot run.");
            return null;
        }

        await auth.RefreshAccountProfileAsync(cancellationToken);
        var gateway = provider.GetRequiredService<IEmailGateway>();
        var page = await gateway.GetMailboxPageAsync(
            new EmailMailboxQuery
            {
                MailboxScope = EmailMailboxScope.AllMail,
                AttachmentsOnly = true,
                PageSize = 50,
            },
            pageToken: null,
            cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var summary in page.Items.OrderByDescending(m => m.ReceivedAt))
        {
            var inboxId = await FindInboxMessageIdAsync(db, summary, cancellationToken);
            if (inboxId is int existing
                && await HasActiveProposalForInboxAsync(db, proposalDefinitionId, existing, cancellationToken))
            {
                continue;
            }

            if (inboxId is int ready)
            {
                var detail =
                    $"inbox id={ready} gmail={summary.MessageId} subject='{TrimSubject(summary.Subject)}'";
                evidence.Pass("S3b Corridor inbox", detail);
                return new CorridorInbox(ready, null, detail);
            }

            if (string.IsNullOrWhiteSpace(summary.InternetMessageId))
            {
                continue;
            }

            var source = new EmailGmailSourceIdentity(
                summary.MessageId,
                summary.InternetMessageId,
                References: null,
                InReplyTo: null,
                summary.Subject,
                summary.From.Value,
                summary.ReceivedAt.UtcDateTime,
                summary.ThreadId);

            var detailMaterialize =
                $"will materialize on CreatePriceQuote gmail={summary.MessageId} "
                + $"subject='{TrimSubject(summary.Subject)}'";
            evidence.Pass("S3b Corridor inbox", detailMaterialize);
            return new CorridorInbox(null, source, detailMaterialize);
        }

        evidence.Fail(
            "S3b Corridor inbox",
            "No AllMail message with attachments and without an active Proposal instance was found.");
        return null;
    }

    internal static async Task<int> ProveAllowlistedCreatePriceQuoteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        CorridorInbox inbox,
        int proposalDefinitionId,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var execution = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        var result = await execution.ExecuteAsync(
            new EmailSuggestedActionExecutionCommand(
                EmailSuggestedActionCodes.CreatePriceQuote,
                inbox.InboxMessageId,
                gate.OperatorUserId,
                inbox.GmailSource));

        if (!result.Succeeded && result.WorkflowInstanceId is not int reused)
        {
            Assert.Fail($"CreatePriceQuote failed: {result.Message}");
        }

        var instanceId = result.WorkflowInstanceId
            ?? await FindActiveProposalInstanceForInboxAsync(
                dbFactory,
                proposalDefinitionId,
                result.InboxMessageId ?? inbox.InboxMessageId,
                evidence);

        Assert.True(instanceId > 0);

        evidence.Pass(
            "S3 Allowlisted CreatePriceQuote succeeded",
            result.Succeeded
                ? $"{WorkflowCodes.Proposal} instance id={instanceId} via email action at "
                  + $"{ProposalStageCodes.ProjectSetup} ({inbox.SelectionDetail})."
                : $"{WorkflowCodes.Proposal} instance id={instanceId} reused from duplicate guard "
                  + $"({result.Message}).");
        evidence.Fact("PRP workflow instance id", instanceId.ToString());
        if (result.InboxMessageId is int inboxId)
        {
            evidence.Fact("Corridor EmailInboxMessage id", inboxId.ToString());
        }

        return instanceId;
    }

    internal static async Task<bool> TryCompleteOpenQuoteProjectAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        int smokeProjectId,
        int operatorUserId,
        PilotSmokeEvidence evidence)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.ProjectAssignments
            .Include(t => t.TaskLinks)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
        {
            return false;
        }

        var emailLink = task.TaskLinks.FirstOrDefault(
            l => l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage);
        if (emailLink is not null)
        {
            var inbox = await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.Id == (int)emailLink.LinkedEntityId);
            if (inbox is not null)
            {
                inbox.ProjectId = smokeProjectId;
                await db.SaveChangesAsync();
            }
        }

        var decision = provider.GetRequiredService<IOpenQuoteProjectDecisionService>();
        var outcome = await decision.CompleteDecisionAsync(
            new OpenQuoteProjectDecisionCommand(
                taskId,
                operatorUserId,
                ReviewCompletionEvents.ReviewProjectCreated,
                TaskResultCodes.ProjectOpened),
            CancellationToken.None);

        if (!outcome.Success)
        {
            evidence.NotRun(
                "S6 PRP corridor",
                $"OpenQuoteProject completion refused: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        return true;
    }

    internal static async Task<bool> TryCompleteFileQuoteMaterialAsync(
        ITaskCompletionService completion,
        int taskId,
        int operatorUserId,
        PilotSmokeEvidence evidence)
    {
        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                taskId,
                ReviewCompletionEvents.ReviewMaterialFiled,
                TaskResultCode: null,
                CompletedTaskLinkIds: null,
                operatorUserId),
            CancellationToken.None);

        if (!outcome.Success)
        {
            evidence.NotRun(
                "S6 PRP corridor",
                $"FileQuoteMaterial completion refused: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        return true;
    }

    private static async Task<int?> FindInboxMessageIdAsync(
        SiNetSQLDbContext db,
        EmailSummary summary,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(summary.InternetMessageId))
        {
            var unique = EmailMessageIdentity.GetMessageUniqueId(
                summary.InternetMessageId,
                summary.MessageId);
            var byUnique = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.MessageUniqueId == unique || m.InternetMessageId == summary.InternetMessageId)
                .OrderByDescending(m => m.Id)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byUnique is > 0)
            {
                return byUnique;
            }
        }

        var gmailKey = $"gmail:{summary.MessageId}";
        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.MessageUniqueId == summary.MessageId || m.MessageUniqueId == gmailKey)
            .OrderByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<bool> HasActiveProposalForInboxAsync(
        SiNetSQLDbContext db,
        int proposalDefinitionId,
        int inboxMessageId,
        CancellationToken cancellationToken) =>
        await db.WorkflowInstances.AsNoTracking().AnyAsync(
            w => w.WorkflowDefinitionId == proposalDefinitionId
                 && w.TriggerType == WorkflowTriggerType.Email
                 && w.TriggerEntityId == inboxMessageId
                 && w.Status != WorkflowStatus.Completed
                 && w.Status != WorkflowStatus.Cancelled,
            cancellationToken);

    private static async Task<int> FindActiveProposalInstanceForInboxAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        int? inboxMessageId,
        PilotSmokeEvidence evidence)
    {
        if (inboxMessageId is not int inboxId || inboxId <= 0)
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkflowInstances.AsNoTracking()
            .Where(w => w.WorkflowDefinitionId == proposalDefinitionId
                        && w.TriggerType == WorkflowTriggerType.Email
                        && w.TriggerEntityId == inboxId
                        && w.Status != WorkflowStatus.Completed
                        && w.Status != WorkflowStatus.Cancelled)
            .OrderByDescending(w => w.Id)
            .Select(w => w.Id)
            .FirstOrDefaultAsync();
    }

    private static string TrimSubject(string subject) =>
        subject.Length <= 80 ? subject : subject[..77] + "...";

    private static string Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 240 ? oneLine : oneLine[..237] + "...";
    }
}
