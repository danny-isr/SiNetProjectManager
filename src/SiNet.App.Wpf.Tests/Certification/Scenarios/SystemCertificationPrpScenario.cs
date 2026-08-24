using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// PRP certification scenario. Starts through <c>IEmailSuggestedActionExecutionService</c> /
/// CreatePriceQuote, walks the production corridor with transition and integrity assertions, and stops at
/// SendQuoteToClient which remains blocked by outbound Gmail policy.
/// </summary>
internal sealed class SystemCertificationPrpScenario : ISystemCertificationScenario
{
    public const string Id = "cert.prp";

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
            ("cert.prp.live_gate", "PRP live writes explicitly enabled after preflight PASS"),
            ("cert.prp.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.prp.gmail_required", "Gmail layer valid for CreatePriceQuote start"),
            ("cert.prp.preconditions", "Proposal definition and seed project prerequisites"),
            ("cert.prp.integrity_baseline", "Integrity baseline before first write"),
            ("cert.prp.inbox", "Resolve inbox row or Gmail source for CreatePriceQuote"),
            ("cert.prp.gmail_identity", "Gmail silent restore matches declared mailbox"),
            ("cert.prp.create_price_quote", "Start PRP through IEmailSuggestedActionExecutionService"),
            ("cert.prp.transition.start", "Post-start stage, task and delta integrity"),
            ("cert.prp.project", "Create [SYS-CERT] project for OpenQuoteProject"),
            ("cert.prp.acc.write", "FileQuoteMaterial ACC move through IEmailMoveToProjectService"),
            ("cert.prp.acc.readback", "Independent IAccFolderBrowserService read-back after ACC write"),
            ("cert.prp.corridor", "Walk PRP corridor through production seams to SendQuote"),
            ("cert.prp.continuation_stage", "Expected continuation stage at SendQuote policy boundary"),
            ("cert.prp.continuation_task", "Expected open SendQuoteToClient task"),
            ("cert.prp.final_delta", "Zero new integrity violations"),
            ("cert.prp.final_absolute", "Absolute integrity clean or approved waivers only"));

        foreach (var taskType in SystemCertificationTransitionAssertions.PrpHappyPathTaskTypes)
        {
            if (string.Equals(taskType, TaskTypeCodes.FileQuoteMaterial, StringComparison.Ordinal))
            {
                continue;
            }

            evidence.Declare(
                $"cert.prp.transition.{taskType}",
                CertificationRequirement.Required,
                $"After completing {taskType}: stage, closed task, single next task, assignee, delta integrity");
        }

        evidence.Declare(
            "cert.prp.transition.FileQuoteMaterial",
            CertificationRequirement.Required,
            "After ACC write + read-back: ReviewMaterialFiled through ITaskCompletionService");

        evidence.Declare(
            "cert.prp.gmail.filing.readback",
            CertificationRequirement.Optional,
            "Gmail label read-back after filing when the production move mutates Gmail");

        evidence.Declare(
            "cert.prp.send_quote",
            CertificationRequirement.Optional,
            "SendQuoteToClient live send — blocked while outbound Gmail is forbidden by policy");

        if (!SystemCertificationEnvironment.IsPrpLiveRequested())
        {
            evidence.Blocked(
                "cert.prp.live_gate",
                $"Set {SystemCertificationEnvironment.PrpLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.prp.live_gate",
            $"{SystemCertificationEnvironment.PrpLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.prp.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.prp.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.TryResolveHeadCommitSha() ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        if (!host.Context.Gmail.IsEnabled || host.Context.Gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.prp.gmail_required",
                "PRP must start through CreatePriceQuote, which requires a valid Gmail layer.");
            return;
        }

        evidence.Pass(
            "cert.prp.gmail_required",
            $"Gmail layer configured for '{host.Context.Gmail.ExpectedAccount}'.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.prp.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationPrpCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var inbox = await SystemCertificationPrpCorridorSupport.TryResolveInboxForCreatePriceQuoteAsync(
            provider,
            dbFactory,
            pre.ProposalDefinitionId,
            host.Context.Gmail,
            evidence,
            cancellationToken);
        if (inbox is null)
        {
            return;
        }

        var instanceId = await SystemCertificationPrpCorridorSupport.ExecuteCreatePriceQuoteAsync(
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

        await SystemCertificationTransitionAssertions.AssertOpenStateAsync(
            dbFactory,
            integrity,
            evidence,
            "cert.prp.transition.start",
            instanceId,
            ProposalStageCodes.ProjectSetup,
            TaskTypeCodes.OpenQuoteProject,
            cancellationToken);

        var projectId = await SystemCertificationPrpCorridorSupport.CreateCertProjectAsync(
            dbFactory, pre, evidence, cancellationToken);
        if (projectId <= 0)
        {
            return;
        }

        var reachedSendQuote = await SystemCertificationPrpCorridorSupport.WalkCorridorUntilSendQuoteAsync(
            provider,
            dbFactory,
            integrity,
            host.Context,
            evidence,
            instanceId,
            projectId,
            host.Context.OperatorUserId,
            cancellationToken);
        if (!reachedSendQuote)
        {
            return;
        }

        evidence.Blocked(
            "cert.prp.send_quote",
            "BLOCKED BY POLICY — outbound Gmail send is forbidden; SendQuoteToClient is not exercised live.");

        await SystemCertificationTransitionAssertions.AssertContinuationStateAsync(
            dbFactory,
            evidence,
            "cert.prp.continuation_stage",
            "cert.prp.continuation_task",
            instanceId,
            ProposalStageCodes.SendQuote,
            TaskTypeCodes.SendQuoteToClient,
            cancellationToken);

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, "cert.prp.final_delta");
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, "cert.prp.final_absolute");

        evidence.RequiresManualCleanup(
            $"SQL rows under [SYS-CERT] project id {projectId}, workflow instance {instanceId}, "
            + "and related tasks — left in place deliberately.");
    }
}
