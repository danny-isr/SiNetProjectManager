using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Resolves the PRP CreatePriceQuote source email from explicit environment variables only.
/// No AllMail scanning or "first suitable message" fallback is permitted.
/// </summary>
internal static class SystemCertificationPrpSourceEmail
{
    internal static async Task<SystemCertificationPrpCorridorSupport.CorridorInbox?> TryResolveExplicitSourceAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        if (!gmail.IsEnabled || gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.prp.source_email",
                "CreatePriceQuote requires a valid Gmail layer; manual IWorkflowCommandService start is forbidden.");
            return null;
        }

        var gmailMessageId = ReadRequiredGmailMessageId(out var envViolation);
        if (gmailMessageId is null)
        {
            evidence.Fail("cert.prp.source_email", envViolation!);
            return null;
        }

        var internetMessageId = ReadOptionalInternetMessageId();

        if (!await VerifyGmailSessionAsync(provider, gmail, evidence, cancellationToken))
        {
            return null;
        }

        var gateway = provider.GetRequiredService<IEmailGateway>();
        var details = await gateway.GetDetailsAsync(gmailMessageId, cancellationToken);
        if (details is null)
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Gmail message '{gmailMessageId}' from {SystemCertificationEnvironment.PrpSourceGmailMessageIdEnv} "
                + "could not be loaded — FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(internetMessageId)
            && !string.Equals(
                details.InternetMessageId?.Trim(),
                internetMessageId,
                StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"{SystemCertificationEnvironment.PrpSourceInternetMessageIdEnv} "
                + $"'{internetMessageId}' does not match loaded message internet id "
                + $"'{details.InternetMessageId ?? "<null>"}'.");
            return null;
        }

        if (!details.HasAttachments)
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Explicit source message '{gmailMessageId}' has no attachments — FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (!SubjectLooksLikeCertificationTestData(details.Subject))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Explicit source subject '{TrimSubject(details.Subject)}' must include "
                + $"'{SystemCertificationEnvironment.CertificationTitlePrefix}' test-data marker — "
                + "FAIL BEFORE PRP WRITE.");
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var inboxId = await FindInboxMessageIdAsync(db, details, cancellationToken);
        if (inboxId is int existing
            && await HasActiveProposalForInboxAsync(db, proposalDefinitionId, existing, cancellationToken))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Explicit source inbox id={existing} already has an active Proposal instance — "
                + "FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (inboxId is int ready)
        {
            var detail =
                $"explicit gmail={gmailMessageId} inbox id={ready} subject='{TrimSubject(details.Subject)}' "
                + $"attachments={details.Attachments.Count}";
            evidence.Pass("cert.prp.source_email", detail);
            evidence.Pass("cert.prp.inbox", detail);
            return new SystemCertificationPrpCorridorSupport.CorridorInbox(ready, null, detail);
        }

        if (string.IsNullOrWhiteSpace(details.InternetMessageId))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Explicit source message '{gmailMessageId}' has no InternetMessageId and no inbox row — "
                + "cannot materialize safely for CreatePriceQuote.");
            return null;
        }

        var source = new EmailGmailSourceIdentity(
            details.MessageId,
            details.InternetMessageId,
            details.References,
            details.InReplyTo,
            details.Subject,
            details.From.Value,
            details.ReceivedAt.UtcDateTime,
            details.ThreadId);

        var materializeDetail =
            $"explicit will materialize gmail={details.MessageId} subject='{TrimSubject(details.Subject)}' "
            + $"attachments={details.Attachments.Count}";
        evidence.Pass("cert.prp.source_email", materializeDetail);
        evidence.Pass("cert.prp.inbox", materializeDetail);
        return new SystemCertificationPrpCorridorSupport.CorridorInbox(null, source, materializeDetail);
    }

    private static string? ReadRequiredGmailMessageId(out string? violation)
    {
        var gmailMessageId = Environment.GetEnvironmentVariable(
            SystemCertificationEnvironment.PrpSourceGmailMessageIdEnv);
        if (string.IsNullOrWhiteSpace(gmailMessageId))
        {
            violation =
                $"{SystemCertificationEnvironment.PrpSourceGmailMessageIdEnv} is required for PRP live — "
                + "no AllMail + AttachmentsOnly mailbox scanning fallback.";
            return null;
        }

        violation = null;
        return gmailMessageId.Trim();
    }

    private static string? ReadOptionalInternetMessageId()
    {
        var value = Environment.GetEnvironmentVariable(
            SystemCertificationEnvironment.PrpSourceInternetMessageIdEnv);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<bool> VerifyGmailSessionAsync(
        IServiceProvider provider,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var auth = provider.GetRequiredService<IConnectorAuthService>();
        var restored = await auth.TryRestoreSessionAsync(cancellationToken);
        if (!restored)
        {
            evidence.Fail(
                "cert.prp.source_email",
                "Gmail token could not be restored silently — CreatePriceQuote cannot run.");
            return false;
        }

        await auth.RefreshAccountProfileAsync(cancellationToken);
        var connected = auth.ConnectedAccountEmail;
        if (!string.Equals(connected?.Trim(), gmail.ExpectedAccount, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Restored Gmail session is '{connected ?? "<unknown>"}' but expected '{gmail.ExpectedAccount}'.");
            return false;
        }

        evidence.Pass("cert.prp.gmail_identity", $"Silent restore authenticated as '{connected}'.");
        return true;
    }

    private static bool SubjectLooksLikeCertificationTestData(string subject) =>
        !string.IsNullOrWhiteSpace(subject)
        && subject.Contains(SystemCertificationEnvironment.CertificationTitlePrefix, StringComparison.Ordinal);

    private static async Task<int?> FindInboxMessageIdAsync(
        SiNetSQLDbContext db,
        EmailMessageDetails details,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(details.InternetMessageId))
        {
            var unique = EmailMessageIdentity.GetMessageUniqueId(
                details.InternetMessageId,
                details.MessageId);
            var byUnique = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.MessageUniqueId == unique || m.InternetMessageId == details.InternetMessageId)
                .OrderByDescending(m => m.Id)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byUnique is > 0)
            {
                return byUnique;
            }
        }

        var gmailKey = $"gmail:{details.MessageId}";
        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.MessageUniqueId == details.MessageId || m.MessageUniqueId == gmailKey)
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

    private static string TrimSubject(string subject) =>
        subject.Length <= 80 ? subject : subject[..77] + "...";
}
