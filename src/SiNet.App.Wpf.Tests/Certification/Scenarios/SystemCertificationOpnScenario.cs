using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// OPN certification scenario. Starts through <c>IEmailSuggestedActionExecutionService</c> /
/// CreateOpinionProject after rebinding the ingested inbox onto a disposable [SYS-CERT] project,
/// walks the production corridor through ACC FileInitialMaterials, and stops at open SendOpinion
/// which remains blocked by outbound-send policy.
/// </summary>
internal sealed class SystemCertificationOpnScenario : ISystemCertificationScenario
{
    public const string Id = "cert.opn";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Opinion"];

    public async ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.opn.live_gate", "OPN live writes explicitly enabled after preflight PASS"),
            ("cert.opn.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.opn.gmail_required", "Gmail layer valid for CreateOpinionProject start"),
            ("cert.opn.preconditions", "Opinion definition and seed project prerequisites"),
            ("cert.opn.integrity_baseline", "Integrity baseline before first write"),
            ("cert.opn.source_email", "Explicit OPN source Gmail message id from environment"),
            ("cert.prp.source_ingest", "Production ACC inbox ingest before CreateOpinionProject"),
            ("cert.prp.source_ingest_sql_readback", "SQL inbox row and attachments match source Gmail"),
            ("cert.prp.source_ingest_acc_readback", "ACC Inbox folder read-back proves ingested files"),
            ("cert.prp.inbox", "Fully ingested inbox row resolved for CreateOpinionProject"),
            ("cert.prp.gmail_identity", "Gmail silent restore matches declared mailbox"),
            ("cert.opn.project", "Create disposable [SYS-CERT] project for project-bound Opinion"),
            ("cert.opn.rebind", "Rebind ingested inbox ProjectId onto the cert project"),
            ("cert.opn.create_opinion_project", "Start OPN through IEmailSuggestedActionExecutionService"),
            ("cert.opn.transition.start", "Post-start stage, task and delta integrity"),
            ("cert.opn.acc.write", "FileInitialMaterials ACC move through IEmailMoveToProjectService"),
            ("cert.opn.acc.readback", "Independent IAccFolderBrowserService read-back after ACC write"),
            ("cert.opn.corridor", "Walk OPN corridor through production seams to SendOpinion"),
            ("cert.opn.continuation_stage", "Expected continuation stage at SendOpinion policy boundary"),
            ("cert.opn.continuation_task", "Expected open SendOpinion task"),
            ("cert.opn.final_delta", "Zero new integrity violations"),
            ("cert.opn.final_absolute", "Absolute integrity clean or approved waivers only"));

        foreach (var taskType in SystemCertificationTransitionAssertions.OpnHappyPathTaskTypes)
        {
            if (string.Equals(taskType, TaskTypeCodes.FileInitialMaterials, StringComparison.Ordinal))
            {
                continue;
            }

            evidence.Declare(
                $"cert.opn.transition.{taskType}",
                CertificationRequirement.Required,
                $"After completing {taskType}: stage, closed task, single next task, assignee, delta integrity");
        }

        evidence.Declare(
            "cert.opn.transition.FileInitialMaterials",
            CertificationRequirement.Required,
            "After ACC write + read-back: ReviewMaterialFiled through ITaskCompletionService");

        evidence.Declare(
            "cert.opn.gmail.filing.readback",
            CertificationRequirement.Optional,
            "Gmail label read-back after filing when the production move mutates Gmail");

        evidence.Declare(
            "cert.opn.send_opinion",
            CertificationRequirement.Optional,
            "SendOpinion live send — blocked while outbound document send is forbidden by policy");

        if (!SystemCertificationEnvironment.IsOpnLiveRequested())
        {
            evidence.Blocked(
                "cert.opn.live_gate",
                $"Set {SystemCertificationEnvironment.OpnLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.opn.live_gate",
            $"{SystemCertificationEnvironment.OpnLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.opn.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.opn.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.ResolveHeadCommitSha().Sha ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        if (!host.Context.Gmail.IsEnabled || host.Context.Gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.opn.gmail_required",
                "OPN must start through CreateOpinionProject, which requires a valid Gmail layer.");
            return;
        }

        evidence.Pass(
            "cert.opn.gmail_required",
            $"Gmail layer configured for '{host.Context.Gmail.ExpectedAccount}'.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.opn.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationOpnCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var inbox = await SystemCertificationPrpSourceEmail.TryResolveExplicitSourceAsync(
            provider,
            dbFactory,
            host.Context,
            pre.OpinionDefinitionId,
            host.Context.Gmail,
            evidence,
            cancellationToken,
            SystemCertificationEnvironment.OpnSourceGmailMessageIdEnv,
            "cert.opn.source_email");
        if (inbox is null)
        {
            return;
        }

        var projectId = await SystemCertificationOpnCorridorSupport.CreateCertProjectAsync(
            dbFactory, pre, evidence, cancellationToken);
        if (projectId <= 0)
        {
            return;
        }

        if (inbox.InboxMessageId is not int inboxMessageId
            || !await SystemCertificationOpnCorridorSupport.RebindInboxToCertProjectAsync(
                dbFactory, inboxMessageId, projectId, evidence, cancellationToken))
        {
            if (inbox.InboxMessageId is null or <= 0)
            {
                evidence.Fail("cert.opn.rebind", "Ingested inbox id missing — cannot rebind for Opinion.");
            }

            return;
        }

        var instanceId = await SystemCertificationOpnCorridorSupport.ExecuteCreateOpinionProjectAsync(
            provider,
            dbFactory,
            inbox,
            pre.OpinionDefinitionId,
            host.Context.OperatorUserId,
            evidence,
            cancellationToken);
        if (instanceId <= 0)
        {
            return;
        }

        if (!await SystemCertificationTransitionAssertions.AssertOpenStateAsync(
                dbFactory,
                integrity,
                evidence,
                "cert.opn.transition.start",
                instanceId,
                OpinionStageCodes.ReceiveMaterial,
                TaskTypeCodes.FileInitialMaterials,
                cancellationToken))
        {
            return;
        }

        var reachedSendOpinion = await SystemCertificationOpnCorridorSupport.WalkCorridorUntilSendOpinionAsync(
            provider,
            dbFactory,
            integrity,
            host.Context,
            evidence,
            instanceId,
            projectId,
            host.Context.OperatorUserId,
            cancellationToken);
        if (!reachedSendOpinion)
        {
            return;
        }

        evidence.Blocked(
            "cert.opn.send_opinion",
            "BLOCKED BY POLICY — outbound document send is forbidden; SendOpinion is not exercised live.");

        await SystemCertificationTransitionAssertions.AssertContinuationStateAsync(
            dbFactory,
            evidence,
            "cert.opn.continuation_stage",
            "cert.opn.continuation_task",
            instanceId,
            OpinionStageCodes.SendOpinion,
            TaskTypeCodes.SendOpinion,
            cancellationToken);

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, "cert.opn.final_delta");
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, "cert.opn.final_absolute");

        evidence.RequiresManualCleanup(
            $"SQL rows under [SYS-CERT] OPN project id {projectId}, workflow instance {instanceId}, "
            + "and related tasks — left in place deliberately.");
    }
}
