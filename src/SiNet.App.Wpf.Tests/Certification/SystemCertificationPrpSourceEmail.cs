using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
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
        SystemCertificationHost.SystemCertificationRunContext context,
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

        if (!string.Equals(details.MessageId, gmailMessageId, StringComparison.Ordinal))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Loaded Gmail message id '{details.MessageId}' != explicit env id '{gmailMessageId}'.");
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

        if (string.IsNullOrWhiteSpace(details.InternetMessageId))
        {
            evidence.Fail(
                "cert.prp.source_email",
                $"Explicit source message '{gmailMessageId}' has no InternetMessageId — "
                + "production ingest and CreatePriceQuote are forbidden.");
            return null;
        }

        evidence.Pass(
            "cert.prp.source_email",
            $"explicit gmail={gmailMessageId} subject='{TrimSubject(details.Subject)}' "
            + $"attachments={details.Attachments.Count}");

        return await SystemCertificationPrpSourceIngest.TryEnsureFullyIngestedAsync(
            provider,
            dbFactory,
            context,
            proposalDefinitionId,
            details,
            evidence,
            cancellationToken);
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

    private static string TrimSubject(string subject) =>
        subject.Length <= 80 ? subject : subject[..77] + "...";
}
