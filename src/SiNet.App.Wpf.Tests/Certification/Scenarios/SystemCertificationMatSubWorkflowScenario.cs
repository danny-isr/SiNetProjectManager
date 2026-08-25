using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// MAT certification via the production PLN→StartSubWorkflow host path (not a standalone email start).
/// Uses the dedicated MAT [SYS-CERT] Gmail fixture as the StartAsync email-trigger identity so
/// FileInitialMaterials inherits ACC work-targets. Stops at open OpenPlanningWorkPackage after
/// MAT.Complete notifies the parent.
/// </summary>
internal sealed class SystemCertificationMatSubWorkflowScenario : ISystemCertificationScenario
{
    public const string Id = "cert.mat";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } =
        ["MaterialIntake", "PlanningWorkflow"];

    public async ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        var steps = SystemCertificationPlnCorridorSupport.EvidenceSteps.Mat;

        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.mat.live_gate", "MAT live writes explicitly enabled after preflight PASS"),
            ("cert.mat.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.mat.gmail_required", "Gmail layer valid for MAT email-trigger identity + ACC filing"),
            (steps.Preconditions, "MaterialIntake + PlanningWorkflow definitions and project prerequisites"),
            ("cert.mat.integrity_baseline", "Integrity baseline before first write"),
            ("cert.mat.source_email", "Explicit MAT source Gmail message id from environment"),
            ("cert.prp.source_ingest", "Production ACC inbox ingest before PLN StartAsync hosting MAT"),
            ("cert.prp.source_ingest_sql_readback", "SQL inbox row and attachments match source Gmail"),
            ("cert.prp.source_ingest_acc_readback", "ACC Inbox folder read-back proves ingested files"),
            ("cert.prp.inbox", "Fully ingested inbox row resolved for MAT-via-PLN StartAsync"),
            ("cert.prp.gmail_identity", "Gmail silent restore matches declared mailbox"),
            (steps.Project, "Create disposable [SYS-CERT] project with planning JobType"),
            (steps.Start, "Start PLN host through IWorkflowCommandService.StartAsync"),
            (steps.TransitionStart, "Post-start FollowWorkOrder open on PLN host"),
            (steps.FollowWorkOrder, "Complete FollowWorkOrder → StartSubWorkflow MaterialIntake"),
            (steps.MatChild, "MaterialIntake child instance under PLN parent"),
            (steps.Filing.AccWrite, "MAT FileInitialMaterials ACC move through IEmailMoveToProjectService"),
            (steps.Filing.AccReadback, "Independent ACC read-back after MAT ACC write"),
            (steps.Filing.Transition, "MAT FileInitialMaterials ReviewMaterialFiled transition"),
            (steps.MatCheck, "MAT CheckQuoteMaterialCompleteness → MaterialComplete"),
            (steps.MatCorridor, "MAT child completes and parent reaches Planning.Start"),
            (steps.ContinuationStage, "Expected parent continuation stage after MAT success"),
            (steps.ContinuationTask, "Expected open OpenPlanningWorkPackage on PLN parent"),
            (steps.FinalDelta, "Zero new integrity violations"),
            (steps.FinalAbsolute, "Absolute integrity clean or approved waivers only"));

        evidence.Declare(
            steps.Filing.GmailFilingReadback,
            CertificationRequirement.Optional,
            "Gmail label read-back after filing when the production move mutates Gmail");

        if (!SystemCertificationEnvironment.IsMatLiveRequested())
        {
            evidence.Blocked(
                "cert.mat.live_gate",
                $"Set {SystemCertificationEnvironment.MatLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.mat.live_gate",
            $"{SystemCertificationEnvironment.MatLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.mat.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.mat.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.ResolveHeadCommitSha().Sha ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        if (!host.Context.Gmail.IsEnabled || host.Context.Gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.mat.gmail_required",
                "MAT live requires a valid Gmail layer for email-trigger identity and ACC filing proof.");
            return;
        }

        evidence.Pass(
            "cert.mat.gmail_required",
            $"Gmail layer configured for '{host.Context.Gmail.ExpectedAccount}'.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.mat.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationPlnCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, steps, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var inbox = await SystemCertificationPrpSourceEmail.TryResolveExplicitSourceAsync(
            provider,
            dbFactory,
            host.Context,
            pre.PlanningDefinitionId,
            host.Context.Gmail,
            evidence,
            cancellationToken,
            SystemCertificationEnvironment.MatSourceGmailMessageIdEnv,
            "cert.mat.source_email");
        if (inbox is null)
        {
            return;
        }

        var projectId = await SystemCertificationPlnCorridorSupport.CreateCertProjectAsync(
            dbFactory, pre, evidence, steps, cancellationToken);
        if (projectId <= 0)
        {
            return;
        }

        var instanceId = await SystemCertificationPlnCorridorSupport.StartPlanningAsync(
            provider,
            pre,
            inbox,
            projectId,
            host.Context.OperatorUserId,
            evidence,
            steps,
            cancellationToken);
        if (instanceId <= 0)
        {
            return;
        }

        if (!await SystemCertificationTransitionAssertions.AssertOpenStateAsync(
                dbFactory,
                integrity,
                evidence,
                steps.TransitionStart,
                instanceId,
                PlanningStageCodes.WorkOrder,
                TaskTypeCodes.FollowWorkOrder,
                cancellationToken))
        {
            return;
        }

        if (!await SystemCertificationPlnCorridorSupport.WalkUntilOpenPlanningWorkPackageAsync(
                provider,
                dbFactory,
                integrity,
                host.Context,
                evidence,
                steps,
                pre,
                instanceId,
                projectId,
                host.Context.OperatorUserId,
                cancellationToken))
        {
            return;
        }

        await SystemCertificationTransitionAssertions.AssertContinuationStateAsync(
            dbFactory,
            evidence,
            steps.ContinuationStage,
            steps.ContinuationTask,
            instanceId,
            PlanningStageCodes.PlanningStart,
            TaskTypeCodes.OpenPlanningWorkPackage,
            cancellationToken);

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, steps.FinalDelta);
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, steps.FinalAbsolute);

        evidence.RequiresManualCleanup(
            $"SQL rows under [SYS-CERT] MAT-via-PLN project id {projectId}, workflow instance {instanceId}, "
            + "MAT child, and related tasks — left in place deliberately.");
    }
}
