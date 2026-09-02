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
        int triggerWorkflowDefinitionId,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default,
        string? gmailMessageIdEnv = null,
        string sourceEmailStep = "cert.prp.source_email")
    {
        gmailMessageIdEnv ??= SystemCertificationEnvironment.PrpSourceGmailMessageIdEnv;

        if (!gmail.IsEnabled || gmail.Violation is not null)
        {
            evidence.Fail(
                sourceEmailStep,
                "PRP email start requires a valid Gmail layer; manual IWorkflowCommandService start is forbidden.");
            return null;
        }

        var gmailMessageId = ReadRequiredGmailMessageId(gmailMessageIdEnv, out var envViolation);
        if (gmailMessageId is null)
        {
            evidence.Fail(sourceEmailStep, envViolation!);
            return null;
        }

        var internetMessageId = ReadOptionalInternetMessageId();

        if (!await VerifyGmailSessionAsync(provider, gmail, evidence, sourceEmailStep, cancellationToken))
        {
            return null;
        }

        var gateway = provider.GetRequiredService<IEmailGateway>();
        var details = await gateway.GetDetailsAsync(gmailMessageId, cancellationToken);
        if (details is null)
        {
            evidence.Fail(
                sourceEmailStep,
                $"Gmail message '{gmailMessageId}' from {gmailMessageIdEnv} "
                + "could not be loaded — FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (!string.Equals(details.MessageId, gmailMessageId, StringComparison.Ordinal))
        {
            evidence.Fail(
                sourceEmailStep,
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
                sourceEmailStep,
                $"{SystemCertificationEnvironment.PrpSourceInternetMessageIdEnv} "
                + $"'{internetMessageId}' does not match loaded message internet id "
                + $"'{details.InternetMessageId ?? "<null>"}'.");
            return null;
        }

        if (!details.HasAttachments)
        {
            evidence.Fail(
                sourceEmailStep,
                $"Explicit source message '{gmailMessageId}' has no attachments — FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (!SubjectLooksLikeCertificationTestData(details.Subject))
        {
            evidence.Fail(
                sourceEmailStep,
                $"Explicit source subject '{TrimSubject(details.Subject)}' must include "
                + $"'{SystemCertificationEnvironment.CertificationTitlePrefix}' test-data marker — "
                + "FAIL BEFORE PRP WRITE.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(details.InternetMessageId))
        {
            evidence.Fail(
                sourceEmailStep,
                $"Explicit source message '{gmailMessageId}' has no InternetMessageId — "
                + "production ingest and PRP email start are forbidden.");
            return null;
        }

        evidence.Pass(
            sourceEmailStep,
            $"explicit gmail={gmailMessageId} subject='{TrimSubject(details.Subject)}' "
            + $"attachments={details.Attachments.Count}");

        return await SystemCertificationPrpSourceIngest.TryEnsureFullyIngestedAsync(
            provider,
            dbFactory,
            context,
            triggerWorkflowDefinitionId,
            details,
            evidence,
            cancellationToken);
    }

    private static string? ReadRequiredGmailMessageId(string gmailMessageIdEnv, out string? violation)
    {
        var gmailMessageId = Environment.GetEnvironmentVariable(gmailMessageIdEnv);
        if (string.IsNullOrWhiteSpace(gmailMessageId))
        {
            violation =
                $"{gmailMessageIdEnv} is required for PRP live — "
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
        string sourceEmailStep,
        CancellationToken cancellationToken)
    {
        var auth = provider.GetRequiredService<IConnectorAuthService>();
        var restored = await auth.TryRestoreSessionAsync(cancellationToken);
        if (!restored)
        {
            evidence.Fail(
                sourceEmailStep,
                "Gmail token could not be restored silently — email start cannot run.");
            return false;
        }

        await auth.RefreshAccountProfileAsync(cancellationToken);
        var connected = auth.ConnectedAccountEmail;
        if (!string.Equals(connected?.Trim(), gmail.ExpectedAccount, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                sourceEmailStep,
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
