using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Common;
using SiNet.Application.Data;
using SiNet.Application.Email.Detail;
using SiNet.Application.DevTools;
using SiNet.Application.Projects;
using SiNet.Application.Settings;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Application.WorkSurfaces;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNet.Infrastructure.Sql.Services.Settings;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// L4W automated P0 Pilot smoke — the live proof of the P1 controls documented in
/// <c>docs/PILOT_CONTROLS.md</c>, against a real DEV database.
/// <para>
/// One ordered scenario, because each step depends on the state the previous step left behind.
/// Every mutation of <c>Pilot.*</c> is snapshotted and restored in <c>finally</c>, and the final
/// assertion is that a fresh read reports <c>Pilot.Enabled=false</c>.
/// </para>
/// <para>
/// Gates and safety model: <c>docs/TEST_STRATEGY.md</c> §4W. Skips (never fails) when the gates are
/// absent.
/// </para>
/// </summary>
[Collection(PilotSmokeTestCollection.Name)]
[Trait("Category", PilotSmokeFactAttribute.Category)]
public sealed class P0PilotLiveSmokeTests
{
    /// <summary>
    /// Deliberate happy-path result per task type. The Proposal workflow offers rejecting branches
    /// beside the advancing ones (for example <c>NotQuoteRequest</c> terminates intake), so the
    /// corridor must never pick a result code blindly.
    /// </summary>
    private static readonly Dictionary<string, string> DesiredResultByTaskType = new(StringComparer.Ordinal)
    {
        [TaskTypeCodes.IdentifyQuoteRequest] = TaskResultCodes.QuoteRequestDetected,
        [TaskTypeCodes.OpenQuoteProject] = TaskResultCodes.ProjectOpened,
        [TaskTypeCodes.CheckQuoteMaterialCompleteness] = TaskResultCodes.MaterialComplete,
        [TaskTypeCodes.PrepareQuoteCalculation] = TaskResultCodes.QuoteCalculationCompleted,
        [TaskTypeCodes.PrepareQuoteDocument] = TaskResultCodes.QuotePrepared,
        [TaskTypeCodes.ApproveQuoteInternal] = TaskResultCodes.QuoteApprovedInternally,
        [TaskTypeCodes.SendQuoteToClient] = TaskResultCodes.QuoteSent,
    };

    [PilotSmokeFact]
    public async Task P0_pilot_controls_hold_against_the_live_dev_database()
    {
        var gate = PilotSmokeEnvironment.TryResolveSqlTier();
        Assert.True(gate.IsEnabled, gate.SkipReason);

        var evidence = PilotSmokeEvidence.Create();
        evidence.Fact("Server", gate.ServerName);
        evidence.Fact("Database", gate.DatabaseName);
        evidence.Fact("Operator SIUser.Id", gate.OperatorUserId.ToString());
        evidence.Fact("Tier", "L4W Category=PilotSmoke (SQL + optional Gmail corridor)");

        var gmailTier = PilotSmokeEnvironment.TryResolveGmailTier();

        await using var provider = BuildProvider(gate.ConnectionString!, gmailTier);
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var settingsQuery = provider.GetRequiredService<ISystemSettingsQueryService>();
        var commands = provider.GetRequiredService<IWorkflowCommandService>();

        var preconditions = await VerifyPreconditionsAsync(provider, dbFactory, gate.OperatorUserId, evidence);
        if (preconditions is null)
        {
            // Reported as Blocked in the evidence file. Failing the test here is correct: the run
            // produced no proof, and a silent pass would be indistinguishable from a green run.
            Assert.Fail(
                "P0 preconditions not met on the target database — see the evidence file: "
                + evidence.MarkdownPath);
            return;
        }

        var snapshot = await ReadPilotSnapshotAsync(dbFactory);
        evidence.Fact("Pilot.Enabled before", snapshot.Enabled ?? "<absent>");
        evidence.Fact("Pilot.AllowedUserIds before", snapshot.AllowedUserIds ?? "<absent>");
        evidence.Fact("Pilot.AllowedWorkflowCodes before", snapshot.AllowedWorkflowCodes ?? "<absent>");

        int? smokeProjectId = null;

        try
        {
            smokeProjectId = await CreateSmokeProjectAsync(dbFactory, preconditions, evidence);
            var projectId = smokeProjectId!.Value;

            await ProveFailClosedAsync(commands, dbFactory, projectId, preconditions, gate, evidence);
            await EnableNarrowPilotAsync(dbFactory, settingsQuery, gate, evidence);

            var corridorInbox = await PilotSmokeCorridorSupport.TryResolveInboxForCreatePriceQuoteAsync(
                provider, dbFactory, preconditions.ProposalDefinitionId, gmailTier, evidence);

            int instanceId;
            if (gmailTier.IsEnabled)
            {
                Assert.NotNull(corridorInbox);
                instanceId = await PilotSmokeCorridorSupport.ProveAllowlistedCreatePriceQuoteAsync(
                    provider, dbFactory, corridorInbox!, preconditions.ProposalDefinitionId, gate, evidence);
            }
            else
            {
                instanceId = await ProveAllowlistedManualStartAsync(
                    commands, dbFactory, projectId, preconditions, gate, evidence);
            }

            await ProveNonAllowlistedUserRejectedAsync(
                commands, dbFactory, projectId, preconditions, gate, evidence);
            await ProveNonAllowlistedCodeRejectedAsync(
                commands, dbFactory, projectId, preconditions, gate, evidence);

            var reachedFollowQuote = await WalkProposalCorridorAsync(
                provider, dbFactory, instanceId, projectId, gate.OperatorUserId, evidence);

            await ProveQuoteApprovalBlockedAsync(
                provider, dbFactory, projectId, instanceId, gate.OperatorUserId, reachedFollowQuote, evidence);

            await ProveKillSwitchAsync(
                provider, dbFactory, projectId, instanceId, preconditions, gate, evidence);
        }
        finally
        {
            await RestorePilotSnapshotAsync(dbFactory, snapshot);

            var finalRead = await settingsQuery.GetSystemSettingsAsync();
            evidence.Fact("Pilot.Enabled after restore (fresh read)", finalRead.Workflow.PilotEnabled.ToString());
            evidence.Fact("Pilot.AllowedUserIds after restore", finalRead.Workflow.PilotAllowedUserIds);
            evidence.Fact("Pilot.AllowedWorkflowCodes after restore", finalRead.Workflow.PilotAllowedWorkflowCodes);

            if (smokeProjectId is int created)
            {
                // Repository policy forbids deleting data; the DEV database is restored from backup.
                evidence.RequiresManualCleanup(
                    $"SQL rows created under project id {created} "
                    + $"(title prefix '{PilotSmokeEnvironment.SmokeTitlePrefix}'): project, workflow "
                    + "instances and tasks. Left in place deliberately — not deleted by the harness.");
            }

            evidence.Fact("Evidence file", evidence.MarkdownPath);
        }

        var afterRestore = await settingsQuery.GetSystemSettingsAsync();
        Assert.False(
            afterRestore.Workflow.PilotEnabled,
            "Pilot.Enabled must read false after the smoke restores its snapshot.");
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider BuildProvider(
        string connectionString,
        PilotSmokeEnvironment.GmailTier? gmailTier = null)
    {
        var services = new ServiceCollection();
        services.AddSiNetLogging();
        services.AddSiNetSql(connectionString);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();

        // The read path under test is the production settings service: it has no DTO cache, which is
        // what makes the kill-switch effective on the next read rather than after a restart.
        services.AddSiNetSystemSettingsSql();

        services.AddSiNetProcessBackbone();
        services.AddSiNetDevTools();
        PilotSmokeCorridorSupport.RegisterCorridorServices(services, gmailTier);

        // IProjectCreateService is deliberately NOT resolved from this graph anywhere in the smoke:
        // SqlProjectCreateService provisions ACC eagerly after commit when an
        // IProjectAccMappingProvisioner is present (docs/ENVIRONMENTS.md §5.1.1).
        return services.BuildServiceProvider();
    }

    private sealed record Preconditions(
        int ProposalDefinitionId,
        int PlanningDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId,
        int NonAllowlistedUserId);

    private static async Task<Preconditions?> VerifyPreconditionsAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int operatorUserId,
        PilotSmokeEvidence evidence)
    {
        var schema = await provider.GetRequiredService<IDatabaseSchemaGate>().ValidateAsync();
        if (!schema.IsReady)
        {
            evidence.Fail(
                "P1 Schema gate",
                $"CanConnect={schema.CanConnect}, SchemaPresent={schema.IsSchemaPresent}, "
                + $"missingTables=[{string.Join(",", schema.MissingTables)}], "
                + $"pendingMigrations=[{string.Join(",", schema.PendingMigrations)}].");
            return null;
        }

        evidence.Pass("P1 Schema gate", "Connect + schema present + no pending migrations.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var operatorUser = await db.Siusers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == operatorUserId);
        if (operatorUser is null || !operatorUser.IsActive)
        {
            evidence.Fail(
                "P2 Operator SIUser",
                $"SIUser.Id={operatorUserId} is missing or inactive on this database.");
            return null;
        }

        // A database restored from the production server carries that server's LoginName, so the
        // Windows identity on this workstation resolves to nobody until the row is repointed. This is
        // the second and last thing the tier fixes rather than reports (docs/TEST_STRATEGY.md §4W.2.3).
        var login = await PilotSmokeSeed.EnsureOperatorLoginAsync(dbFactory, operatorUserId);
        evidence.Pass(
            "P2 Operator SIUser",
            $"Id={operatorUserId} name='{operatorUser.Name}' active, and Windows identity "
            + $"'{login.WindowsLogin}' resolves to it"
            + (login.Changed
                ? $" after repointing LoginName from '{login.PreviousLoginName ?? "<empty>"}'. "
                  + "Group memberships and role were not touched."
                : " already; nothing was changed.")
            + $" Role={operatorUser.Role}.");

        if (login.Changed)
        {
            evidence.RequiresManualCleanup(
                $"SIUser {operatorUserId}.LoginName was changed from "
                + $"'{login.PreviousLoginName ?? "<empty>"}' to '{login.WindowsLogin}'. Intentional and "
                + "persistent; it is what lets this workstation authenticate at all.");
        }

        var proposalId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();
        var planningId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow && d.IsActive)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();

        if (proposalId == 0 || planningId == 0)
        {
            evidence.Fail(
                "P3 Workflow definitions",
                $"{WorkflowCodes.Proposal}={proposalId}, {WorkflowCodes.PlanningWorkflow}={planningId}. "
                + "Both must exist and be active. Reporting Blocked rather than seeding.");
            return null;
        }

        evidence.Pass(
            "P3 Workflow definitions",
            $"{WorkflowCodes.Proposal} id={proposalId}, {WorkflowCodes.PlanningWorkflow} id={planningId}.");

        // The PRP→PLN proof needs a project type whose default continuation is PlanningWorkflow.
        // Proposal itself is email-driven and is NOT auto-mapped to any ProjectType in seed data
        // (SqlWorkflowSeedService); the smoke starts PRP with IsProjectBound=false, matching production.
        var planningJobTypeId = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .Where(m => m.WorkflowDefinitionId == planningId && m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.ProjectTypeId)
            .FirstOrDefaultAsync();

        if (planningJobTypeId == 0)
        {
            evidence.Fail(
                "P4 JobType mapped to PlanningWorkflow",
                "No enabled ProjectTypeWorkflowDefinition maps a project type to "
                + $"{WorkflowCodes.PlanningWorkflow}. Without it the QuoteApprovedByClient proof "
                + "would pass trivially. Reporting Blocked rather than seeding.");
            return null;
        }

        evidence.Pass(
            "P4 JobType mapped to PlanningWorkflow",
            $"project type id={planningJobTypeId} maps to {WorkflowCodes.PlanningWorkflow}.");

        // The one lookup row this tier creates rather than reports on: a restored database has no
        // Place titled 'SI', and without it no project here could carry a development-derived ACC
        // name. See docs/TEST_STRATEGY.md §4W.2.2.
        var (placeId, placeCreated) = await PilotSmokeSeed.EnsureSiPlaceAsync(dbFactory, operatorUserId);
        evidence.Pass(
            "P5 Place 'SI'",
            $"Place id={placeId} "
            + (placeCreated ? "created by this run." : "already existed."));
        if (placeCreated)
        {
            evidence.RequiresManualCleanup(
                $"Place id {placeId} titled '{PilotSmokeEnvironment.RequiredAccPlaceTitle}' — created "
                + "by this run. Harmless to leave; it reappears on re-run.");
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (company is null || contact is null)
        {
            evidence.Fail(
                "P6 Company / Contact",
                "Project creation requires an existing company and contact; none found.");
            return null;
        }

        evidence.Pass("P6 Company / Contact", $"company id={company.Id}, contact id={contact.Id}.");

        var otherUserId = await db.Siusers
            .AsNoTracking()
            .Where(u => u.Id != operatorUserId && u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (otherUserId == 0)
        {
            evidence.Skipped(
                "P7 Second identity",
                "No second active SIUser — the non-allowlisted user proof will use a synthetic id, "
                + "which still exercises the allowlist because the gate rejects before any DB write.");
        }
        else
        {
            evidence.Pass("P7 Second identity", $"SIUser.Id={otherUserId} available.");
        }

        var seedVerify = await TryVerifySeedBaselineAsync(dbFactory, evidence);
        if (seedVerify is false)
        {
            return null;
        }

        return new Preconditions(
            proposalId,
            planningId,
            placeId,
            company.Id,
            contact.Id,
            planningJobTypeId,
            otherUserId == 0 ? operatorUserId + 1_000_000 : otherUserId);
    }

    private static async Task<bool?> TryVerifySeedBaselineAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        PilotSmokeEvidence evidence)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbFactory);
        services.AddSiNetDevTools();
        await using var sp = services.BuildServiceProvider();

        var verify = sp.GetService<ISeedBaselineVerifyService>();
        if (verify is null)
        {
            evidence.Skipped("P8 Seed baseline", "ISeedBaselineVerifyService is not registered.");
            return null;
        }

        var result = await verify.VerifyAsync();
        if (result.HasRequiredGaps)
        {
            evidence.Fail(
                "P8 Seed baseline",
                "Required gaps: workflows=["
                + string.Join(",", result.MissingWorkflowDefinitionCodes)
                + "] groups=[" + string.Join(",", result.MissingUserGroupCodes)
                + "] catalog=[" + string.Join(",", result.MissingProjectFileCatalogCodes)
                + "] jobTypesWithoutWorkflow=["
                + string.Join(",", result.JobTypesMissingWorkflowMapping) + "].");
            return false;
        }

        evidence.Pass(
            "P8 Seed baseline",
            result.IsComplete
                ? "Complete."
                : "No required gaps; prerequisite warnings present "
                  + $"(JobTypePresent={result.JobTypePresent}, "
                  + $"CorrespondenceFolderPresent={result.CorrespondenceFolderPresent}).");
        return true;
    }

    private static async Task<int> CreateSmokeProjectAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        Preconditions pre,
        PilotSmokeEvidence evidence)
    {
        // Constructed directly with only the DbContext factory: no IProjectFolderBootstrapper and no
        // IProjectAccMappingProvisioner, so creation cannot reach ACC or the file server.
        var creator = new SqlProjectCreateService(dbFactory);

        var title = $"{PilotSmokeEnvironment.SmokeTitlePrefix} {DateTime.Now:MMdd-HHmm}";

        var result = await creator.CreateAsync(
            new CreateProjectCommand(
                Title: title,
                PlaceId: pre.PlaceId,
                CompanyId: pre.CompanyId,
                ContactId: pre.ContactId,
                JobTypeIds: new[] { pre.PlanningJobTypeId }),
            CancellationToken.None);

        Assert.True(result.Succeeded, $"Smoke project creation failed: {result.ErrorMessage}");
        var projectId = result.ProjectId!.Value;

        evidence.Pass(
            "S0 Smoke project created",
            $"id={projectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}' "
            + $"projectType={pre.PlanningJobTypeId} (PLN continuation mapping only; PRP starts "
            + "IsProjectBound=false like email-driven production; no ACC provisioner).");
        evidence.Fact("Smoke project id", projectId.ToString());
        return projectId;
    }

    private static async Task ProveFailClosedAsync(
        IWorkflowCommandService commands,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        Preconditions pre,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        await WritePilotSettingsAsync(dbFactory, enabled: null, userIds: null, codes: null);

        var before = await CountProjectWorkflowStateAsync(dbFactory, projectId);
        var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
            commands.StartAsync(
                new StartWorkflowCommand(
                    pre.ProposalDefinitionId,
                    projectId,
                    WorkflowTriggerTypeDto.Manual,
                    null,
                    gate.OperatorUserId,
                    Notes: "P0 smoke fail-closed probe",
                    IsProjectBound: false),
                CancellationToken.None).AsTask());

        var after = await CountProjectWorkflowStateAsync(dbFactory, projectId);

        Assert.Equal(before, after);
        evidence.Pass(
            "S1 Fail-closed root Start rejected",
            $"Pilot.* rows absent. Rejected with: {Trim(ex.Message)}. "
            + $"Instances/tasks unchanged ({after.Instances} instances, {after.Tasks} tasks).");
    }

    private static async Task EnableNarrowPilotAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        ISystemSettingsQueryService settingsQuery,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        await WritePilotSettingsAsync(
            dbFactory,
            enabled: "true",
            userIds: gate.OperatorUserId.ToString(),
            codes: WorkflowCodes.Proposal);

        var read = await settingsQuery.GetSystemSettingsAsync();
        Assert.True(read.Workflow.PilotEnabled);
        Assert.Equal(gate.OperatorUserId.ToString(), read.Workflow.PilotAllowedUserIds);
        Assert.Equal(WorkflowCodes.Proposal, read.Workflow.PilotAllowedWorkflowCodes);

        evidence.Pass(
            "S2 Narrow enable",
            $"Pilot.Enabled=true, AllowedUserIds={gate.OperatorUserId}, "
            + $"AllowedWorkflowCodes={WorkflowCodes.Proposal}. A fresh read reflects it immediately "
            + "(no settings cache), so the kill-switch needs no restart. PlanningWorkflow stays out.");
    }

    private static async Task<int> ProveAllowlistedManualStartAsync(
        IWorkflowCommandService commands,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        Preconditions pre,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var start = await commands.StartAsync(
            new StartWorkflowCommand(
                pre.ProposalDefinitionId,
                projectId,
                WorkflowTriggerTypeDto.Manual,
                null,
                gate.OperatorUserId,
                Notes: "P0 smoke allowlisted start",
                IsProjectBound: false),
            CancellationToken.None);

        Assert.True(start.Instance.Id > 0);

        await using var db = await dbFactory.CreateDbContextAsync();
        var instances = await db.WorkflowInstances
            .AsNoTracking()
            .CountAsync(i => i.ProjectId == projectId);
        Assert.Equal(1, instances);

        evidence.Pass(
            "S3 Allowlisted Start succeeded",
            $"{WorkflowCodes.Proposal} instance id={start.Instance.Id} (manual IsProjectBound=false — "
            + "Gmail corridor off); exactly one instance on the project.");
        evidence.Fact("PRP workflow instance id", start.Instance.Id.ToString());
        return start.Instance.Id;
    }

    private static async Task ProveNonAllowlistedUserRejectedAsync(
        IWorkflowCommandService commands,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        Preconditions pre,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var before = await CountProjectWorkflowStateAsync(dbFactory, projectId);
        var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
            commands.StartAsync(
                new StartWorkflowCommand(
                    pre.ProposalDefinitionId,
                    projectId,
                    WorkflowTriggerTypeDto.Manual,
                    null,
                    pre.NonAllowlistedUserId,
                    Notes: "P0 smoke non-allowlisted user probe",
                    IsProjectBound: false),
                CancellationToken.None).AsTask());
        var after = await CountProjectWorkflowStateAsync(dbFactory, projectId);

        Assert.Equal(before, after);
        Assert.Contains("AllowedUserIds", ex.Message, StringComparison.Ordinal);

        evidence.Pass(
            "S4 Non-allowlisted user rejected",
            $"UserId={pre.NonAllowlistedUserId} (allowlist holds only {gate.OperatorUserId}) "
            + $"rejected with: {Trim(ex.Message)}. No new instance or task.");
    }

    private static async Task ProveNonAllowlistedCodeRejectedAsync(
        IWorkflowCommandService commands,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        Preconditions pre,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        var before = await CountProjectWorkflowStateAsync(dbFactory, projectId);
        var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
            commands.StartAsync(
                new StartWorkflowCommand(
                    pre.PlanningDefinitionId,
                    projectId,
                    WorkflowTriggerTypeDto.Manual,
                    null,
                    // The allowlisted operator: this proves the code allowlist, not the user one.
                    gate.OperatorUserId,
                    Notes: "P0 smoke non-allowlisted code probe"),
                CancellationToken.None).AsTask());
        var after = await CountProjectWorkflowStateAsync(dbFactory, projectId);

        Assert.Equal(before, after);
        Assert.Contains("AllowedWorkflowCodes", ex.Message, StringComparison.Ordinal);

        await using var db = await dbFactory.CreateDbContextAsync();
        var planningInstances = await db.WorkflowInstances
            .AsNoTracking()
            .CountAsync(i => i.ProjectId == projectId && i.WorkflowDefinitionId == pre.PlanningDefinitionId);
        Assert.Equal(0, planningInstances);

        evidence.Pass(
            "S5 Non-allowlisted workflow code rejected",
            $"{WorkflowCodes.PlanningWorkflow} start by the allowlisted operator rejected with: "
            + $"{Trim(ex.Message)}. No PLN instance exists on the project.");
    }

    /// <summary>
    /// Advances the live PRP instance using only the production seam: resolve the work-surface
    /// context for the open task, then <see cref="ITaskCompletionService"/>. Never mutates task or
    /// stage rows directly, and never uses Ops Advance.
    /// </summary>
    private static async Task<bool> WalkProposalCorridorAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId,
        int smokeProjectId,
        int operatorUserId,
        PilotSmokeEvidence evidence)
    {
        var navigation = provider.GetRequiredService<ITaskNavigationService>();
        var completion = provider.GetRequiredService<ITaskCompletionService>();

        for (var step = 1; step <= 16; step++)
        {
            var open = await FindOpenTaskAsync(dbFactory, instanceId);
            if (open is null)
            {
                evidence.NotRun(
                    "S6 PRP corridor",
                    $"No open task on instance {instanceId} after {step - 1} completion(s); "
                    + "the corridor closed before reaching FollowQuoteApproval.");
                return false;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FollowQuoteApproval, StringComparison.Ordinal))
            {
                evidence.Pass(
                    "S6 PRP corridor",
                    $"Reached {TaskTypeCodes.FollowQuoteApproval} (task id={open.TaskId}) after "
                    + $"{step - 1} completion(s) through production seams only.");
                return true;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.OpenQuoteProject, StringComparison.Ordinal))
            {
                var ok = await PilotSmokeCorridorSupport.TryCompleteOpenQuoteProjectAsync(
                    provider, dbFactory, open.TaskId, smokeProjectId, operatorUserId, evidence);
                if (!ok)
                {
                    return false;
                }

                continue;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FileQuoteMaterial, StringComparison.Ordinal))
            {
                var ok = await PilotSmokeCorridorSupport.TryCompleteFileQuoteMaterialAsync(
                    completion, open.TaskId, operatorUserId, evidence);
                if (!ok)
                {
                    return false;
                }

                continue;
            }

            var context = await navigation.ResolveAsync(open.TaskId, CancellationToken.None);
            if (context?.CompletionEventCode is not { Length: > 0 } eventCode)
            {
                evidence.NotRun(
                    "S6 PRP corridor",
                    $"Task {open.TaskId} ({open.TaskTypeCode}) has no resolvable CompletionEventCode, "
                    + "so it cannot be completed through the production seam. Stopping rather than "
                    + "mutating rows directly.");
                return false;
            }

            var resultCode = ChooseResultCode(open.TaskTypeCode, context.AllowedResultCodes);
            if (resultCode is null && context.AllowedResultCodes.Count > 0)
            {
                evidence.NotRun(
                    "S6 PRP corridor",
                    $"Task {open.TaskId} ({open.TaskTypeCode}) offers ambiguous results ["
                    + string.Join(", ", context.AllowedResultCodes)
                    + "] with no declared happy-path choice. Stopping rather than guessing a branch.");
                return false;
            }

            var outcome = await completion.CompleteAsync(
                new CompleteTaskCommand(open.TaskId, eventCode, resultCode, null, operatorUserId),
                CancellationToken.None);

            if (!outcome.Success)
            {
                evidence.NotRun(
                    "S6 PRP corridor",
                    $"Task {open.TaskId} ({open.TaskTypeCode}) completion refused by business rules: "
                    + $"{Trim(outcome.ErrorMessage)}. This is the application behaving normally on a "
                    + "surface that needs real work product; the corridor stops here.");
                return false;
            }
        }

        evidence.NotRun(
            "S6 PRP corridor",
            "Completed 16 tasks without reaching FollowQuoteApproval — bailing out of the loop.");
        return false;
    }

    private static string? ChooseResultCode(string taskTypeCode, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return null;
        }

        if (DesiredResultByTaskType.TryGetValue(taskTypeCode, out var desired)
            && allowed.Contains(desired, StringComparer.Ordinal))
        {
            return desired;
        }

        return allowed.Count == 1 ? allowed[0] : null;
    }

    /// <summary>
    /// The critical P1 proof. Always runs the direct pre-validation against live data, and when the
    /// corridor actually reached FollowQuoteApproval it additionally proves the real completion path
    /// refuses and leaves the task open.
    /// </summary>
    private static async Task ProveQuoteApprovalBlockedAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        int instanceId,
        int operatorUserId,
        bool reachedFollowQuote,
        PilotSmokeEvidence evidence)
    {
        var starter = provider.GetRequiredService<IProjectTypeContinuationStarter>();
        var validation = await starter.ValidateBeforeQuoteApprovalAsync(projectId, operatorUserId);

        Assert.False(
            validation.Success,
            "PlanningWorkflow is outside Pilot.AllowedWorkflowCodes, so the continuation "
            + "pre-validation must refuse.");
        Assert.Contains(WorkflowCodes.PlanningWorkflow, validation.Error, StringComparison.Ordinal);

        evidence.Pass(
            "S7a QuoteApproved pre-validation refuses",
            $"ValidateBeforeQuoteApprovalAsync(project={projectId}, user={operatorUserId}) failed with: "
            + $"{Trim(validation.Error)}");

        if (!reachedFollowQuote)
        {
            evidence.NotRun(
                "S7b QuoteApprovedByClient via ITaskCompletionService",
                "The corridor did not reach FollowQuoteApproval, so the completion path could not be "
                + "exercised live. Not manufacturing a path. S7a still proves the same policy on the "
                + "same data through the method SqlTaskCompletionService calls.");
            return;
        }

        var open = await FindOpenTaskAsync(dbFactory, instanceId);
        Assert.NotNull(open);
        Assert.Equal(TaskTypeCodes.FollowQuoteApproval, open!.TaskTypeCode);

        var navigation = provider.GetRequiredService<ITaskNavigationService>();
        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var context = await navigation.ResolveAsync(open.TaskId, CancellationToken.None);
        var eventCode = context?.CompletionEventCode;

        if (string.IsNullOrWhiteSpace(eventCode))
        {
            evidence.NotRun(
                "S7b QuoteApprovedByClient via ITaskCompletionService",
                $"No CompletionEventCode resolved for task {open.TaskId}.");
            return;
        }

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                open.TaskId, eventCode, TaskResultCodes.QuoteApprovedByClient, null, operatorUserId),
            CancellationToken.None);

        Assert.False(outcome.Success, "QuoteApprovedByClient must fail while PLN is blocked.");
        Assert.False(outcome.TaskClosed, "The approval task must stay open.");

        var stillOpen = await FindOpenTaskAsync(dbFactory, instanceId);
        Assert.NotNull(stillOpen);
        Assert.Equal(open.TaskId, stillOpen!.TaskId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var planningInstances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
            .CountAsync(i => i.ProjectId == projectId
                          && i.WorkflowDefinition!.Code == WorkflowCodes.PlanningWorkflow);
        Assert.Equal(0, planningInstances);

        evidence.Pass(
            "S7b QuoteApprovedByClient via ITaskCompletionService",
            $"Refused before mutation: {Trim(outcome.ErrorMessage)}. Task {open.TaskId} still open, "
            + "no PLN root instance created.");
    }

    private static async Task ProveKillSwitchAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId,
        int instanceId,
        Preconditions pre,
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEvidence evidence)
    {
        await WritePilotSettingsAsync(
            dbFactory,
            enabled: "false",
            userIds: gate.OperatorUserId.ToString(),
            codes: WorkflowCodes.Proposal);

        var commands = provider.GetRequiredService<IWorkflowCommandService>();
        var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
            commands.StartAsync(
                new StartWorkflowCommand(
                    pre.ProposalDefinitionId,
                    projectId,
                    WorkflowTriggerTypeDto.Manual,
                    null,
                    gate.OperatorUserId,
                    Notes: "P0 smoke kill-switch probe",
                    IsProjectBound: false),
                CancellationToken.None).AsTask());

        Assert.Contains("Pilot.Enabled", ex.Message, StringComparison.Ordinal);
        evidence.Pass(
            "S8a Kill-switch blocks new root Start",
            $"Immediately after flipping Pilot.Enabled=false, on the same provider and with no "
            + $"restart: {Trim(ex.Message)}");

        var open = await FindOpenTaskAsync(dbFactory, instanceId);
        if (open is null)
        {
            evidence.NotRun(
                "S8b Existing instance still advances",
                "No open task remains on the smoke instance, so completion under a disabled Pilot "
                + "could not be exercised. Not manufacturing a path.");
            return;
        }

        if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FollowQuoteApproval, StringComparison.Ordinal))
        {
            evidence.NotRun(
                "S8b Existing instance still advances",
                $"The only open task is {TaskTypeCodes.FollowQuoteApproval}, whose advancing result is "
                + "deliberately blocked by S7. Completing it here would confuse the two proofs.");
            return;
        }

        var navigation = provider.GetRequiredService<ITaskNavigationService>();
        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var context = await navigation.ResolveAsync(open.TaskId, CancellationToken.None);
        if (context?.CompletionEventCode is not { Length: > 0 } eventCode)
        {
            evidence.NotRun(
                "S8b Existing instance still advances",
                $"No CompletionEventCode resolved for open task {open.TaskId} ({open.TaskTypeCode}).");
            return;
        }

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                open.TaskId,
                eventCode,
                ChooseResultCode(open.TaskTypeCode, context.AllowedResultCodes),
                null,
                gate.OperatorUserId),
            CancellationToken.None);

        Assert.True(
            outcome.Success,
            $"Pilot.Enabled=false must not block completion on an existing instance: {outcome.ErrorMessage}");

        evidence.Pass(
            "S8b Existing instance still advances",
            $"With Pilot.Enabled=false, task {open.TaskId} ({open.TaskTypeCode}) completed "
            + $"(TaskClosed={outcome.TaskClosed}, WorkflowAdvanced={outcome.WorkflowAdvanced}).");
    }

    private sealed record OpenTask(int TaskId, string TaskTypeCode);

    private static async Task<OpenTask?> FindOpenTaskAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Where(t => t.AssignmentStatus!.IsOpen
                     && t.TaskLinks.Any(l =>
                            l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                         && l.LinkedEntityId == instanceId))
            .OrderBy(t => t.Id)
            .Select(t => new OpenTask(t.Id, t.TaskType!.Code))
            .FirstOrDefaultAsync();
    }

    private sealed record WorkflowStateCount(int Instances, int Tasks);

    private static async Task<WorkflowStateCount> CountProjectWorkflowStateAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int projectId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var instances = await db.WorkflowInstances.AsNoTracking().CountAsync(i => i.ProjectId == projectId);
        var tasks = await db.ProjectAssignments.AsNoTracking().CountAsync(t => t.ProjectId == projectId);
        return new WorkflowStateCount(instances, tasks);
    }

    private sealed record PilotSnapshot(string? Enabled, string? AllowedUserIds, string? AllowedWorkflowCodes);

    private static async Task<PilotSnapshot> ReadPilotSnapshotAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.SettingKey == SystemSettingKeys.PilotEnabled
                     || s.SettingKey == SystemSettingKeys.PilotAllowedUserIds
                     || s.SettingKey == SystemSettingKeys.PilotAllowedWorkflowCodes)
            .ToListAsync();

        return new PilotSnapshot(
            rows.FirstOrDefault(r => r.SettingKey == SystemSettingKeys.PilotEnabled)?.SettingValue,
            rows.FirstOrDefault(r => r.SettingKey == SystemSettingKeys.PilotAllowedUserIds)?.SettingValue,
            rows.FirstOrDefault(r => r.SettingKey == SystemSettingKeys.PilotAllowedWorkflowCodes)?.SettingValue);
    }

    /// <summary>
    /// Restores the exact pre-run state, including removing rows this run created. A row that did
    /// not exist before must not be left behind as an explicit <c>false</c>, so the "absent means
    /// fail-closed" path stays the one ops will actually meet.
    /// </summary>
    private static async Task RestorePilotSnapshotAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        PilotSnapshot snapshot)
    {
        await WritePilotSettingsAsync(
            dbFactory,
            snapshot.Enabled,
            snapshot.AllowedUserIds,
            snapshot.AllowedWorkflowCodes);
    }

    /// <summary>
    /// Direct EF upsert of the three <c>Pilot.*</c> rows. Deliberately not
    /// <c>ISystemSettingsCommandService</c>: that path requires an authenticated admin identity and
    /// would also rewrite every other managed setting. A <see langword="null"/> value deletes the row.
    /// </summary>
    private static async Task WritePilotSettingsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string? enabled,
        string? userIds,
        string? codes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        await UpsertAsync(db, SystemSettingKeys.PilotEnabled, enabled);
        await UpsertAsync(db, SystemSettingKeys.PilotAllowedUserIds, userIds);
        await UpsertAsync(db, SystemSettingKeys.PilotAllowedWorkflowCodes, codes);

        await db.SaveChangesAsync();

        static async Task UpsertAsync(SiNetSQLDbContext db, string key, string? value)
        {
            var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key);
            if (value is null)
            {
                if (row is not null)
                {
                    db.SystemSettings.Remove(row);
                }

                return;
            }

            if (row is null)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = value,
                    LastUpdated = DateTime.UtcNow,
                });
            }
            else
            {
                row.SettingValue = value;
                row.LastUpdated = DateTime.UtcNow;
            }
        }
    }

    private static string Trim(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "<no message>";
        }

        var single = message.ReplaceLineEndings(" ").Trim();
        return single.Length <= 300 ? single : single[..300] + "…";
    }
}
