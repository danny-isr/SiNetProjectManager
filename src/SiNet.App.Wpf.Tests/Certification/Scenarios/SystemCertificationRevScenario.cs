using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// REV certification scenario. Starts through <c>IEmailSuggestedActionExecutionService</c> /
/// CreateNewReview, walks OpenReviewProject → MAT child (ACC filing) → no-police happy path →
/// REV.Completed.
/// </summary>
internal sealed class SystemCertificationRevScenario : ISystemCertificationScenario
{
    public const string Id = "cert.rev";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Review"];

    public async ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.rev.live_gate", "REV live writes explicitly enabled after preflight PASS"),
            ("cert.rev.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.rev.gmail_required", "Gmail layer valid for CreateNewReview start"),
            ("cert.rev.preconditions", "Review + MaterialIntake definitions and cert project prerequisites"),
            ("cert.rev.integrity_baseline", "Integrity baseline before first write"),
            ("cert.rev.source_email", "Explicit REV source Gmail message id from environment"),
            ("cert.prp.source_ingest", "Production ACC inbox ingest before CreateNewReview"),
            ("cert.prp.source_ingest_sql_readback", "SQL inbox row and attachments match source Gmail"),
            ("cert.prp.source_ingest_acc_readback", "ACC Inbox folder read-back proves ingested files"),
            ("cert.prp.inbox", "Fully ingested inbox row resolved for CreateNewReview"),
            ("cert.prp.gmail_identity", "Gmail silent restore matches declared mailbox"),
            ("cert.rev.create_new_review", "Start REV through IEmailSuggestedActionExecutionService"),
            ("cert.rev.transition.start", "Post-start REV.ProjectSetup / OpenReviewProject integrity"),
            ("cert.rev.project", "Create disposable [SYS-CERT] project for OpenReviewProject"),
            ("cert.rev.mat.child", "MaterialIntake child spawned under REV.MaterialIntake"),
            ("cert.rev.mat.corridor", "Walk MAT child through ACC filing to MAT.Complete"),
            ("cert.rev.transition.MaterialIntake", "Parent reaches REV.ProfessionalReview after MAT child"),
            ("cert.rev.corridor", "Walk REV happy path (no police) to REV.Completed"),
            ("cert.rev.terminal", "Review instance Completed with zero open driving tasks"),
            ("cert.rev.mat.terminal", "MAT child terminal with zero open tasks"),
            ("cert.rev.final_delta", "Zero new integrity violations"),
            ("cert.rev.final_absolute", "Absolute integrity clean or approved waivers only"));

        foreach (var taskType in SystemCertificationTransitionAssertions.RevHappyPathTaskTypes)
        {
            if (string.Equals(taskType, TaskTypeCodes.OpenReviewProject, StringComparison.Ordinal))
            {
                continue;
            }

            evidence.Declare(
                $"cert.rev.transition.{taskType}",
                CertificationRequirement.Required,
                $"After completing {taskType}: stage, closed task, single next task, assignee, delta integrity");
        }

        evidence.Declare(
            "cert.rev.transition.OpenReviewProject",
            CertificationRequirement.Required,
            "After OpenReviewProject: REV.MaterialIntake sub-workflow host + delta integrity");

        evidence.Declare(
            "cert.rev.mat.transition.CheckQuoteMaterialCompleteness",
            CertificationRequirement.Required,
            "After ACC write + read-back: ReviewMaterialCheckCompleted on MAT child");

        evidence.Declare(
            "cert.rev.mat.transition.FileInitialMaterials",
            CertificationRequirement.Required,
            "After ACC write + read-back: ReviewMaterialFiled through ITaskCompletionService");

        evidence.Declare(
            "cert.rev.mat.acc.write",
            CertificationRequirement.Required,
            "MAT FileInitialMaterials ACC move through IEmailMoveToProjectService");

        evidence.Declare(
            "cert.rev.mat.acc.readback",
            CertificationRequirement.Required,
            "Independent ACC read-back after MAT filing");

        evidence.Declare(
            "cert.rev.mat.gmail.filing.readback",
            CertificationRequirement.Optional,
            "Gmail label read-back after MAT filing when production move mutates Gmail");

        if (!SystemCertificationEnvironment.IsRevLiveRequested())
        {
            evidence.Blocked(
                "cert.rev.live_gate",
                $"Set {SystemCertificationEnvironment.RevLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.rev.live_gate",
            $"{SystemCertificationEnvironment.RevLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.rev.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.rev.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.ResolveHeadCommitSha().Sha ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        if (!host.Context.Gmail.IsEnabled || host.Context.Gmail.Violation is not null)
        {
            evidence.Fail(
                "cert.rev.gmail_required",
                "REV must start through CreateNewReview, which requires a valid Gmail layer.");
            return;
        }

        evidence.Pass(
            "cert.rev.gmail_required",
            $"Gmail layer configured for '{host.Context.Gmail.ExpectedAccount}'.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.rev.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationRevCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var inbox = await SystemCertificationPrpSourceEmail.TryResolveExplicitSourceAsync(
            provider,
            dbFactory,
            host.Context,
            pre.ReviewDefinitionId,
            host.Context.Gmail,
            evidence,
            cancellationToken,
            SystemCertificationEnvironment.RevSourceGmailMessageIdEnv,
            "cert.rev.source_email");
        if (inbox is null)
        {
            return;
        }

        var instanceId = await SystemCertificationRevCorridorSupport.ExecuteCreateNewReviewAsync(
            provider,
            inbox,
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
                "cert.rev.transition.start",
                instanceId,
                ReviewStageCodes.ProjectSetup,
                TaskTypeCodes.OpenReviewProject,
                cancellationToken))
        {
            return;
        }

        var projectId = await SystemCertificationRevCorridorSupport.CreateCertProjectAsync(
            dbFactory, pre, evidence, cancellationToken);
        if (projectId <= 0)
        {
            return;
        }

        var openAtSetup = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, instanceId, cancellationToken);
        if (openAtSetup.Count != 1
            || !string.Equals(openAtSetup[0].TaskTypeCode, TaskTypeCodes.OpenReviewProject, StringComparison.Ordinal))
        {
            evidence.Fail("cert.rev.transition.OpenReviewProject", "OpenReviewProject task missing at ProjectSetup.");
            return;
        }

        if (!await SystemCertificationRevCorridorSupport.TryCompleteOpenReviewProjectAsync(
                provider,
                dbFactory,
                openAtSetup[0].TaskId,
                projectId,
                host.Context.OperatorUserId,
                evidence,
                cancellationToken))
        {
            return;
        }

        var (walked, matInstanceId) = await SystemCertificationRevCorridorSupport.WalkMatAndRevHappyPathAsync(
            provider,
            dbFactory,
            integrity,
            host.Context,
            evidence,
            pre,
            instanceId,
            projectId,
            host.Context.OperatorUserId,
            cancellationToken);
        if (!walked)
        {
            return;
        }

        evidence.Pass("cert.rev.corridor", "REV no-police happy path reached REV.Completed.");

        if (!await SystemCertificationRevCorridorSupport.AssertTerminalCompletedAsync(
                dbFactory, evidence, instanceId, matInstanceId, cancellationToken))
        {
            return;
        }

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, "cert.rev.final_delta");
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, "cert.rev.final_absolute");

        evidence.RequiresManualCleanup(
            $"SQL rows under [SYS-CERT] REV project id {projectId}, workflow instance {instanceId}, "
            + $"MAT child {matInstanceId}, and related tasks — left in place deliberately.");
    }
}
