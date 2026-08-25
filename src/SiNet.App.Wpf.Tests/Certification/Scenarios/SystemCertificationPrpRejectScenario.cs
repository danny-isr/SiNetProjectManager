using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// PRP RejectPriceQuote certification. Starts through
/// <see cref="SiNet.Application.Email.Detail.IEmailSuggestedActionExecutionService"/> /
/// RejectPriceQuote and asserts terminal <c>PRP.Rejected</c> / Completed with no open driving tasks.
/// Does not walk the happy-path corridor and does not send outbound Gmail.
/// </summary>
internal sealed class SystemCertificationPrpRejectScenario : ISystemCertificationScenario
{
    public const string Id = "cert.prp.reject";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Proposal"];

    public async ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.prp.reject.live_gate", "PRP RejectPriceQuote live writes explicitly enabled after preflight PASS"),
            ("cert.prp.reject.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.prp.reject.gmail_required", "Gmail layer valid for RejectPriceQuote start"),
            ("cert.prp.preconditions", "Proposal definition and seed project prerequisites"),
            ("cert.prp.reject.integrity_baseline", "Integrity baseline before first write"),
            ("cert.prp.reject.source_email", "Explicit PRP reject source Gmail message id from environment"),
            ("cert.prp.source_ingest", "Production ACC inbox ingest before RejectPriceQuote"),
            ("cert.prp.source_ingest_sql_readback", "SQL inbox row and attachments match source Gmail"),
            ("cert.prp.source_ingest_acc_readback", "ACC Inbox folder read-back proves ingested files"),
            ("cert.prp.inbox", "Fully ingested inbox row resolved for RejectPriceQuote"),
            ("cert.prp.gmail_identity", "Gmail silent restore matches declared mailbox"),
            ("cert.prp.reject.execute", "Start PRP reject through IEmailSuggestedActionExecutionService"),
            ("cert.prp.reject.terminal_stage", "Terminal stage PRP.Rejected"),
            ("cert.prp.reject.terminal_status", "WorkflowStatus.Completed on rejected instance"),
            ("cert.prp.reject.no_open_tasks", "Zero open driving tasks after reject"),
            ("cert.prp.reject.final_delta", "Zero new integrity violations"),
            ("cert.prp.reject.final_absolute", "Absolute integrity clean or approved waivers only"));

        if (!SystemCertificationEnvironment.IsPrpRejectLiveRequested())
        {
            evidence.Blocked(
                "cert.prp.reject.live_gate",
                $"Set {SystemCertificationEnvironment.PrpRejectLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.prp.reject.live_gate",
            $"{SystemCertificationEnvironment.PrpRejectLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.prp.reject.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.prp.reject.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.ResolveHeadCommitSha().Sha ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        if (!host.Context.Gmail.IsEnabled || host.Context.Gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.prp.reject.gmail_required",
                "RejectPriceQuote requires a valid Gmail layer.");
            return;
        }

        evidence.Pass(
            "cert.prp.reject.gmail_required",
            $"Gmail layer configured for '{host.Context.Gmail.ExpectedAccount}'.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.prp.reject.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationPrpCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var inbox = await SystemCertificationPrpSourceEmail.TryResolveExplicitSourceAsync(
            provider,
            dbFactory,
            host.Context,
            pre.ProposalDefinitionId,
            host.Context.Gmail,
            evidence,
            cancellationToken,
            SystemCertificationEnvironment.PrpRejectSourceGmailMessageIdEnv,
            "cert.prp.reject.source_email");
        if (inbox is null)
        {
            return;
        }

        var instanceId = await SystemCertificationPrpCorridorSupport.ExecuteRejectPriceQuoteAsync(
            provider,
            dbFactory,
            inbox,
            pre.ProposalDefinitionId,
            host.Context.OperatorUserId,
            evidence,
            cancellationToken);
        if (instanceId <= 0)
        {
            return;
        }

        if (!await SystemCertificationPrpCorridorSupport.AssertRejectTerminalAsync(
                dbFactory, evidence, instanceId, cancellationToken))
        {
            return;
        }

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, "cert.prp.reject.final_delta");
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, "cert.prp.reject.final_absolute");

        evidence.RequiresManualCleanup(
            $"SQL rows for RejectPriceQuote workflow instance {instanceId} and related inbox "
            + $"{inbox.InboxMessageId?.ToString() ?? "<unknown>"} — left in place deliberately.");
    }
}
