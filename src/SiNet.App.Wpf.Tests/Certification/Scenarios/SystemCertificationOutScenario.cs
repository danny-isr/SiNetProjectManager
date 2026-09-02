using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification.Scenarios;

/// <summary>
/// OUT certification scenario (contract A). Starts through <c>IWorkflowCommandService.StartAsync</c>
/// on a disposable [SYS-CERT] project and completes ReceiveOutsourceQuote → ApproveOutsourceQuote →
/// MonitorOutsourcePayments through production task completion seams only.
/// </summary>
internal sealed class SystemCertificationOutScenario : ISystemCertificationScenario
{
    public const string Id = "cert.out";

    public string ScenarioId => Id;

    public IReadOnlyList<string> WorkflowDefinitionCodes { get; } = ["Outsourcing"];

    public async ValueTask RunAsync(
        SystemCertificationHost.AuthorizedWriteHost host,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(evidence);

        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("cert.out.live_gate", "OUT live writes explicitly enabled after preflight PASS"),
            ("cert.out.preflight_evidence", "Saved DEV preflight report is CERTIFIED"),
            ("cert.out.preconditions", "Outsourcing definition and cert project prerequisites"),
            ("cert.out.integrity_baseline", "Integrity baseline before first write"),
            ("cert.out.project", "Create disposable [SYS-CERT] project for OUT StartAsync"),
            ("cert.out.start", "Start OUT through IWorkflowCommandService.StartAsync"),
            ("cert.out.transition.start", "Post-start OUT.ReceiveOffer / ReceiveOutsourceQuote integrity"),
            ("cert.out.corridor", "Walk OUT contract A through production task completion seams"),
            ("cert.out.terminal", "Outsourcing Completed with zero open tasks and no dangling TaskLinks"),
            ("cert.out.final_delta", "Zero new integrity violations"),
            ("cert.out.final_absolute", "Absolute integrity clean or approved waivers only"));

        foreach (var taskType in SystemCertificationTransitionAssertions.OutHappyPathTaskTypes)
        {
            evidence.Declare(
                $"cert.out.transition.{taskType}",
                CertificationRequirement.Required,
                $"After completing {taskType}: prior task closed, next stage open, single required task, delta integrity");
        }

        if (!SystemCertificationEnvironment.IsOutLiveRequested())
        {
            evidence.Blocked(
                "cert.out.live_gate",
                $"Set {SystemCertificationEnvironment.OutLiveEnabledEnv}=1 only after DEV Preflight PASS "
                + "and operator approval.");
            return;
        }

        evidence.Pass(
            "cert.out.live_gate",
            $"{SystemCertificationEnvironment.OutLiveEnabledEnv}=1.");

        var preflightViolation = SystemCertificationPreflightEvidence.TryValidate(
            host.Target,
            host.Context.Gmail,
            host.Context.Acc,
            out var preflightPath);
        if (preflightViolation is not null)
        {
            evidence.Fail("cert.out.preflight_evidence", preflightViolation);
            return;
        }

        evidence.Pass(
            "cert.out.preflight_evidence",
            $"Bound CERTIFIED preflight evidence at '{preflightPath}' matches current target, layers, "
            + $"commit {SystemCertificationGitMetadata.ResolveHeadCommitSha().Sha ?? "<unknown>"}, "
            + $"and freshness <= {SystemCertificationPreflightBinding.MaxAge.TotalHours:0}h.");

        var provider = host.Provider;
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var integrity = new SystemCertificationIntegrityValidator(dbFactory);
        await integrity.BaselineAsync(cancellationToken);
        evidence.Pass("cert.out.integrity_baseline", "pre-write baseline captured");

        var pre = await SystemCertificationOutCorridorSupport.TryResolvePreconditionsAsync(
            dbFactory, evidence, cancellationToken);
        if (pre is null)
        {
            return;
        }

        var projectId = await SystemCertificationOutCorridorSupport.CreateCertProjectAsync(
            dbFactory, pre, evidence, cancellationToken);
        if (projectId <= 0)
        {
            return;
        }

        var instanceId = await SystemCertificationOutCorridorSupport.StartOutsourcingAsync(
            provider,
            pre,
            projectId,
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
                "cert.out.transition.start",
                instanceId,
                OutsourcingStageCodes.ReceiveOffer,
                TaskTypeCodes.ReceiveOutsourceQuote,
                cancellationToken))
        {
            return;
        }

        if (!await SystemCertificationOutCorridorSupport.WalkContractAAsync(
                provider,
                dbFactory,
                integrity,
                evidence,
                instanceId,
                host.Context.OperatorUserId,
                cancellationToken))
        {
            return;
        }

        evidence.Pass("cert.out.corridor", "OUT contract A reached OUT.Complete through production seams.");

        var finalReport = await integrity.CheckAsync(cancellationToken);
        SystemCertificationAssertions.AssertDeltaClean(finalReport, evidence, "cert.out.final_delta");
        SystemCertificationAssertions.AssertAbsoluteClean(finalReport, evidence, "cert.out.final_absolute");

        evidence.RequiresManualCleanup(
            $"SQL rows under [SYS-CERT] OUT project id {projectId}, workflow instance {instanceId}, "
            + "and related tasks — left in place deliberately.");
    }
}
