using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;
using SiNet.Application.Workflow;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class PilotStartGateTests
{
    private const int UserId = 1;
    private const int OtherUserId = 99;

    [Fact]
    public async Task Root_Start_when_Pilot_disabled_then_rejected()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, PilotEnabled: false, "1", WorkflowCodes.Proposal));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
                commands.StartAsync(
                    new StartWorkflowCommand(defId, 100, WorkflowTriggerTypeDto.Manual, null, UserId, Notes: null),
                    CancellationToken.None).AsTask());

            Assert.Contains("Pilot.Enabled", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Root_Start_when_user_not_allowlisted_then_rejected()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.Proposal));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
                commands.StartAsync(
                    new StartWorkflowCommand(defId, 100, WorkflowTriggerTypeDto.Manual, null, OtherUserId, Notes: null),
                    CancellationToken.None).AsTask());

            Assert.Contains("AllowedUserIds", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Root_Start_when_workflow_not_allowlisted_then_rejected()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.Opinion));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
                commands.StartAsync(
                    new StartWorkflowCommand(defId, 100, WorkflowTriggerTypeDto.Manual, null, UserId, Notes: null),
                    CancellationToken.None).AsTask());

            Assert.Contains("AllowedWorkflowCodes", ex.Message, StringComparison.Ordinal);
            Assert.Contains(WorkflowCodes.Proposal, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Root_Start_when_user_and_code_allowed_then_proceeds()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.Proposal));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var start = await commands.StartAsync(
                new StartWorkflowCommand(defId, 100, WorkflowTriggerTypeDto.Manual, null, UserId, Notes: null),
                CancellationToken.None);

            Assert.True(start.Instance.Id > 0);
        }
    }

    [Fact]
    public async Task Ops_Manual_Start_uses_same_Pilot_gate()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.Proposal));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    defId, 100, WorkflowTriggerTypeDto.Manual, null, UserId, Notes: "ops"),
                CancellationToken.None);

            Assert.True(start.Instance.Id > 0);
        }
    }

    [Fact]
    public async Task System_continuation_Start_uses_same_Pilot_gate()
    {
        var (provider, defId) = await BuildProviderAsync(
            new WorkflowSystemSettingsDto(2, PilotEnabled: false, UserId.ToString(), WorkflowCodes.Proposal));

        await using (provider)
        {
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var ex = await Assert.ThrowsAsync<WorkflowStartPreflightException>(() =>
                commands.StartAsync(
                    new StartWorkflowCommand(
                        defId, 100, WorkflowTriggerTypeDto.System, null, UserId,
                        Notes: "post QuoteApprovedByClient", IsProjectBound: true, JobTypeId: 1),
                    CancellationToken.None).AsTask());

            Assert.Contains("Pilot.Enabled", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Child_engine_Start_with_parent_bypasses_root_Pilot_gate()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new ProposalWorkflowHarness.StubDbContextFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Projects.Add(new Project { Id = 100, Title = "P" });
            db.Siusers.Add(new Siuser { Id = UserId, Name = "U", IsActive = true });
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = 10,
                Code = WorkflowCodes.MaterialIntake,
                Name = "MAT",
                IsActive = true,
            });
            db.WorkflowStageDefinitions.Add(new WorkflowStageDefinition
            {
                Id = 1,
                WorkflowDefinitionId = 10,
                Code = "MAT.Start",
                Name = "Start",
                IsInitial = true,
                IsFinal = true,
                NodeType = "End",
                SortOrder = 1,
            });
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = 50,
                ProjectId = 100,
                WorkflowDefinitionId = 10,
                Status = WorkflowStatus.Active,
                CreatedByUserId = UserId,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        // Fail-closed Pilot (no settings) — root Start would deny; child path must still work.
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<WorkflowEngine>();
        var child = await engine.StartAsync(
            definitionId: 10,
            projectId: 100,
            triggerType: WorkflowTriggerType.System,
            triggerEntityId: null,
            userId: UserId,
            notes: "child",
            ct: CancellationToken.None,
            isProjectBound: true,
            parentWorkflowInstanceId: 50);

        Assert.True(child.Id > 0);
        Assert.Equal(50, child.ParentWorkflowInstanceId);
    }

    [Fact]
    public async Task ValidateBeforeQuoteApproval_when_PLN_blocked_then_fails_with_acting_UserId()
    {
        var (factory, projectId) = await BuildContinuationProjectAsync();
        var gate = new SqlPilotStartGate(
            factory,
            new StubPilotSystemSettingsQueryService(
                new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.Proposal)));

        var starter = new SqlProjectTypeContinuationStarter(factory, new NoOpWorkflowCommands(), gate);
        var result = await starter.ValidateBeforeQuoteApprovalAsync(projectId, UserId);

        Assert.False(result.Success);
        Assert.Contains(WorkflowCodes.PlanningWorkflow, result.Error, StringComparison.Ordinal);
        Assert.Contains("פיילוט", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateBeforeQuoteApproval_uses_command_UserId_not_zero()
    {
        var (factory, projectId) = await BuildContinuationProjectAsync();
        var gate = new SqlPilotStartGate(
            factory,
            new StubPilotSystemSettingsQueryService(
                new WorkflowSystemSettingsDto(
                    2,
                    true,
                    OtherUserId.ToString(),
                    WorkflowCodes.PlanningWorkflow)));

        var starter = new SqlProjectTypeContinuationStarter(factory, new NoOpWorkflowCommands(), gate);

        var asOther = await starter.ValidateBeforeQuoteApprovalAsync(projectId, OtherUserId);
        Assert.True(asOther.Success, asOther.Error);

        var asUser1 = await starter.ValidateBeforeQuoteApprovalAsync(projectId, UserId);
        Assert.False(asUser1.Success);
        Assert.Contains(UserId.ToString(), asUser1.Error, StringComparison.Ordinal);
        Assert.Contains("AllowedUserIds", asUser1.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateBeforeQuoteApproval_when_continuation_allowed_then_ok()
    {
        var (factory, projectId) = await BuildContinuationProjectAsync();
        var gate = new SqlPilotStartGate(
            factory,
            new StubPilotSystemSettingsQueryService(
                new WorkflowSystemSettingsDto(2, true, UserId.ToString(), WorkflowCodes.PlanningWorkflow)));

        var starter = new SqlProjectTypeContinuationStarter(factory, new NoOpWorkflowCommands(), gate);
        var result = await starter.ValidateBeforeQuoteApprovalAsync(projectId, UserId);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task Task_completion_auto_advance_when_Pilot_disabled_still_works()
    {
        // Existing instance advance is not a root Start — must succeed even with Pilot off.
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var (projectId, emailId, defId) =
                await ProposalWorkflowHarness.SeedProjectAndEmailAsync(options);
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var start = await commands.StartAsync(
                new StartWorkflowCommand(defId, projectId, WorkflowTriggerTypeDto.Email, emailId, UserId, Notes: null),
                CancellationToken.None);

            var intake = await ProposalWorkflowHarness.GetOpenStageTaskAsync(
                options, start.Instance.Id, TaskTypeCodes.IdentifyQuoteRequest);
            await ProposalWorkflowHarness.MarkTaskResultAsync(
                options, intake.Id, TaskResultCodes.QuoteRequestDetected);

            // Rebind DI with Pilot disabled; advance existing instance through same DB.
            var services = new ServiceCollection();
            services.AddSingleton(provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>());
            services.AddSingleton<ISystemSettingsQueryService>(
                new StubPilotSystemSettingsQueryService(
                    new WorkflowSystemSettingsDto(2, PilotEnabled: false, UserId.ToString(), WorkflowCodes.Proposal)));
            services.AddSiNetProcessBackbone();
            await using var disabledPilot = services.BuildServiceProvider();

            var advance = await disabledPilot.GetRequiredService<IWorkflowCommandService>()
                .CheckAndAutoAdvanceAsync(new TaskClosedCommand(intake.Id, UserId), CancellationToken.None);

            Assert.NotNull(advance);
            Assert.Equal(StageCompletionActionDto.AutoAdvanced, advance!.Action);
        }
    }

    [Fact]
    public void PilotStartPolicy_fail_closed_defaults()
    {
        var workflow = new WorkflowSystemSettingsDto(2);
        Assert.False(PilotStartPolicy.IsRootStartAllowed(workflow, 1, WorkflowCodes.Proposal, out var reason));
        Assert.Contains("Pilot.Enabled", reason, StringComparison.Ordinal);
    }

    private static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, int DefId)> BuildProviderAsync(
        WorkflowSystemSettingsDto workflow)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new ProposalWorkflowHarness.StubDbContextFactory(options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Projects.Add(new Project { Id = 100, Title = "Pilot Project" });
            db.Siusers.Add(new Siuser { Id = UserId, Name = "Pilot", IsActive = true });
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = 10,
                Code = WorkflowCodes.Proposal,
                Name = "Proposal",
                IsActive = true,
            });
            db.WorkflowStageDefinitions.Add(new WorkflowStageDefinition
            {
                Id = 1,
                WorkflowDefinitionId = 10,
                Code = "PRP.Start",
                Name = "Start",
                IsInitial = true,
                IsFinal = true,
                NodeType = "End",
                SortOrder = 1,
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSingleton<ISystemSettingsQueryService>(new StubPilotSystemSettingsQueryService(workflow));
        services.AddSiNetProcessBackbone();
        return (services.BuildServiceProvider(), 10);
    }

    private static async Task<(IDbContextFactory<SiNetSQLDbContext> Factory, int ProjectId)> BuildContinuationProjectAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new ProposalWorkflowHarness.StubDbContextFactory(options);

        await using var db = await factory.CreateDbContextAsync();
        db.Projects.Add(new Project { Id = 100, Title = "Cont" });
        db.JobTypes.Add(new JobType { Id = 1, Title = "תכנון" });
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = 10,
            Code = WorkflowCodes.PlanningWorkflow,
            Name = "Planning",
            IsActive = true,
        });
        db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject { ProjectId = 100, ProjectTypeId = 1 });
        db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
        {
            ProjectTypeId = 1,
            WorkflowDefinitionId = 10,
            IsDefault = true,
            IsEnabled = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();
        return (factory, 100);
    }

    private sealed class NoOpWorkflowCommands : IWorkflowCommandService
    {
        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct) =>
            throw new InvalidOperationException("Start should not be called during pre-validation.");

        public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct) =>
            ValueTask.FromResult<StageCompletionResultDto?>(null);

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct) =>
            ValueTask.FromResult<StageCompletionResultDto?>(null);

        public ValueTask<StageCompletionResultDto?> CheckAndAdvanceOnActionCompletedAsync(ActionCompletedCommand command, CancellationToken ct) =>
            ValueTask.FromResult<StageCompletionResultDto?>(null);

        public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct) =>
            ValueTask.FromResult(0);

        public ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
