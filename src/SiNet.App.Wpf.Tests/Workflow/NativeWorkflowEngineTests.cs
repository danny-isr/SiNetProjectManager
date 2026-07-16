using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Application.Email.Detail;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

/// <summary>
/// End-to-end coverage for the native workflow engine re-homed into
/// <c>SiNet.Infrastructure.Sql</c> (Phase 5). Proves the whole graph —
/// <c>NativeWorkflowCommandService</c> → <c>WorkflowTaskOrchestrator</c> →
/// <c>WorkflowEngine</c> / <c>WorkflowTransitionEvaluator</c> /
/// <c>WorkflowStageTaskProvisioningService</c> / <c>WorkflowActionExecutor</c> —
/// drives a real seeded Proposal (PRP.*) workflow WITHOUT the legacy SiNetSQL
/// engine, and that task-close + auto-advance run atomically through
/// <see cref="ITaskCompletionService"/> (Phase 1d shared context).
/// </summary>
public sealed class NativeWorkflowEngineTests
{
    private const int UserId = 1;

    [Fact]
    public async Task Native_engine_starts_Proposal_and_auto_advances_creating_stage_tasks()
    {
        var (provider, options) = await BuildSeededProviderAsync();
        await using (provider)
        {
            var (projectId, emailId, defId) = await SeedProjectAndEmailAsync(options);

            var commands = provider.GetRequiredService<IWorkflowCommandService>();

            // ── Start Proposal (email-driven, not project-bound) ────────────
            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    DefinitionId: defId,
                    ProjectId: projectId,
                    TriggerType: WorkflowTriggerTypeDto.Email,
                    TriggerEntityId: emailId,
                    UserId: UserId,
                    Notes: "native e2e",
                    IsProjectBound: false),
                CancellationToken.None);

            Assert.NotNull(start.Instance);
            var instanceId = start.Instance.Id;

            // Initial stage must have provisioned the IdentifyQuoteRequest task.
            var intake = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.IdentifyQuoteRequest);
            Assert.True(intake.AssignedToId.HasValue, "Intake task must be assigned.");

            // ── Intake → ProjectSetup (QuoteRequestDetected) ────────────────
            await MarkTaskCompletedAsync(options, intake.Id, TaskResultCodes.QuoteRequestDetected);
            var afterIntake = await commands.CheckAndAutoAdvanceAsync(
                new TaskClosedCommand(intake.Id, UserId), CancellationToken.None);

            Assert.NotNull(afterIntake);
            Assert.Equal(StageCompletionActionDto.AutoAdvanced, afterIntake!.Action);
            await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.ProjectSetup);

            var openProject = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.OpenQuoteProject);

            // ── ProjectSetup → FileMaterial (ProjectOpened) ─────────────────
            await MarkTaskCompletedAsync(options, openProject.Id, TaskResultCodes.ProjectOpened);
            var afterOpen = await commands.CheckAndAutoAdvanceAsync(
                new TaskClosedCommand(openProject.Id, UserId), CancellationToken.None);

            Assert.NotNull(afterOpen);
            Assert.Equal(StageCompletionActionDto.AutoAdvanced, afterOpen!.Action);
            await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.FileMaterial);

            // The re-homed provisioning must materialize a real, routable task.
            var fileMaterial = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.FileQuoteMaterial);
            Assert.NotNull(fileMaterial.TaskTypeId);
            Assert.True(fileMaterial.TaskTypeId > 0);
        }
    }

    [Fact]
    public async Task Task_completion_shares_context_and_atomically_auto_advances()
    {
        var (provider, options) = await BuildSeededProviderAsync();
        await using (provider)
        {
            var (projectId, emailId, defId) = await SeedProjectAndEmailAsync(options);
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var completion = provider.GetRequiredService<ITaskCompletionService>();

            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    defId, projectId, WorkflowTriggerTypeDto.Email, emailId, UserId, "shared-tx", IsProjectBound: false),
                CancellationToken.None);
            var instanceId = start.Instance.Id;

            // Walk to FileMaterial through the native auto-advance path.
            var intake = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.IdentifyQuoteRequest);
            await MarkTaskCompletedAsync(options, intake.Id, TaskResultCodes.QuoteRequestDetected);
            await commands.CheckAndAutoAdvanceAsync(new TaskClosedCommand(intake.Id, UserId), CancellationToken.None);

            var openProject = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.OpenQuoteProject);
            await MarkTaskCompletedAsync(options, openProject.Id, TaskResultCodes.ProjectOpened);
            await commands.CheckAndAutoAdvanceAsync(new TaskClosedCommand(openProject.Id, UserId), CancellationToken.None);

            var fileMaterial = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.FileQuoteMaterial);

            // ── The shared-context closure: ITaskCompletionService closes the
            //    FileQuoteMaterial task AND runs the workflow auto-advance on the
            //    same DbContext (Phase 1d). One call must both close and advance. ─
            var result = await completion.CompleteAsync(
                new CompleteTaskCommand(
                    TaskId: fileMaterial.Id,
                    CompletionEventCode: ReviewCompletionEvents.ReviewMaterialFiled,
                    TaskResultCode: null,
                    CompletedTaskLinkIds: null,
                    UserId: UserId),
                CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.TaskClosed);
            Assert.True(result.WorkflowAdvanced);
            Assert.False(result.WorkflowAdvancePending);
            Assert.NotNull(result.StageAdvanceResult);

            await AssertCurrentStageAsync(options, instanceId, ProposalStageCodes.MaterialCheck);

            // Auto-advance provisioned the next stage's task on the same shared write.
            var check = await GetOpenStageTaskAsync(options, instanceId, TaskTypeCodes.CheckQuoteMaterialCompleteness);
            Assert.True(check.AssignedToId.HasValue);
        }
    }

    [Fact]
    public void Process_backbone_wires_native_command_service_and_new_action_handlers()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetProcessBackbone();
        using var provider = services.BuildServiceProvider();

        // The IWorkflowCommandService port must be fulfilled by the native engine,
        // not the fail-fast UnboundWorkflowCommandService.
        var commands = provider.GetRequiredService<IWorkflowCommandService>();
        Assert.Equal("NativeWorkflowCommandService", commands.GetType().Name);

        // The re-homed transition action handlers must be dispatchable.
        var actions = provider.GetRequiredService<IProcessActionService>();
        Assert.True(actions.HasHandler(ProcessActionCodes.CloseProject));
        Assert.True(actions.HasHandler(ProcessActionCodes.StartSubWorkflow));
        Assert.True(actions.HasHandler(ProcessActionCodes.ClosePreviousStageTasks));
    }

    [Fact]
    public async Task ActionCompleted_auto_transition_advances_through_native_command_service()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new StubDbContextFactory(options);

        var (instanceId, _, _) = await SeedActionCompletedScenarioAsync(factory);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();

        var commands = provider.GetRequiredService<IWorkflowCommandService>();

        // The matching workflow-advancing action reports Completed → the native engine
        // (replacing the legacy WorkflowActionCompletedHandler) auto-advances to the final stage.
        var result = await commands.CheckAndAdvanceOnActionCompletedAsync(
            new ActionCompletedCommand(instanceId, "ApproveOrClose", "Succeeded", UserId),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(StageCompletionActionDto.AutoAdvanced, result!.Action);
        Assert.NotNull(result.AdvancedInstance);

        await using var db = new SiNetSQLDbContext(options);
        var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
        Assert.Equal(WorkflowStatus.Completed, instance.Status);
    }

    [Fact]
    public async Task ActionCompleted_with_non_matching_action_does_not_advance()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new StubDbContextFactory(options);

        var (instanceId, stageAId, _) = await SeedActionCompletedScenarioAsync(factory);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();

        var commands = provider.GetRequiredService<IWorkflowCommandService>();

        // A different action code has no matching ActionCompleted transition → no advance.
        var result = await commands.CheckAndAdvanceOnActionCompletedAsync(
            new ActionCompletedCommand(instanceId, "SomeUnrelatedAction", "Succeeded", UserId),
            CancellationToken.None);

        Assert.Null(result);

        await using var db = new SiNetSQLDbContext(options);
        var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == instanceId);
        Assert.Equal(WorkflowStatus.Active, instance.Status);
        Assert.Equal(stageAId, instance.CurrentStageId);
    }

    /// <summary>
    /// Seeds a minimal 2-stage definition (A → B) with a single ActionCompleted Auto transition rule
    /// matching action code <c>ApproveOrClose</c>, plus one Active instance parked at stage A.
    /// Returns the instance id and both stage ids.
    /// </summary>
    private static async Task<(int InstanceId, int StageAId, int StageBId)> SeedActionCompletedScenarioAsync(
        IDbContextFactory<SiNetSQLDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.Siusers.Add(new Siuser { Id = UserId, Name = "Test User", IsActive = true });

        var status = new ProjectStatus { Code = "AC_ACTIVE", Title = "Active", IsActive = true, SortOrder = 1 };
        db.ProjectStatuses.Add(status);
        await db.SaveChangesAsync();

        var project = new Project { Title = "ActionCompleted test", ProjectStatusId = status.Id };
        db.Projects.Add(project);

        var def = new WorkflowDefinition { Code = "TEST_AC", Name = "Action Completed Test", IsActive = true };
        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();

        var stageA = new WorkflowStageDefinition
        {
            WorkflowDefinitionId = def.Id,
            Code = "A",
            Name = "Await Decision",
            NodeType = "Stage",
            IsInitial = true,
            SortOrder = 1,
        };
        var stageB = new WorkflowStageDefinition
        {
            WorkflowDefinitionId = def.Id,
            Code = "B",
            Name = "Done",
            NodeType = "End",
            IsFinal = true,
            SortOrder = 2,
        };
        db.WorkflowStageDefinitions.AddRange(stageA, stageB);
        await db.SaveChangesAsync();

        const string conditionJson = "{\"ActionCode\":\"ApproveOrClose\"}";
        db.WorkflowTransitionRules.Add(new WorkflowTransitionRule
        {
            WorkflowDefinitionId = def.Id,
            FromStageId = stageA.Id,
            ToStageId = stageB.Id,
            Name = "On ApproveOrClose",
            TriggerType = WorkflowTransitionTriggerType.ActionCompleted,
            ConditionType = WorkflowTransitionConditionType.ActionCompleted,
            ConditionJson = conditionJson,
            ConditionHash = WorkflowTransitionRule.ComputeConditionHash(conditionJson),
            EvaluationMode = WorkflowEvaluationMode.Auto,
            Priority = 1,
        });

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = def.Id,
            ProjectId = project.Id,
            IsProjectBound = false,
            Status = WorkflowStatus.Active,
            CurrentStageId = stageA.Id,
            TriggerType = WorkflowTriggerType.Email,
            CreatedByUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        return (instance.Id, stageA.Id, stageB.Id);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Phase 3e — email suggested action starts native Proposal workflow
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Email_CreatePriceQuote_action_starts_native_Proposal_workflow()
    {
        var (provider, options) = await BuildSeededProviderAsync();
        await using (provider)
        {
            var (_, emailId, defId) = await SeedProjectAndEmailAsync(options);
            var execution = BuildExecutionService(provider);

            var result = await execution.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    EmailSuggestedActionCodes.CreatePriceQuote, emailId, UserId),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.RequiresFollowUp);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .SingleOrDefaultAsync(w =>
                    w.WorkflowDefinitionId == defId
                    && w.TriggerType == WorkflowTriggerType.Email
                    && w.TriggerEntityId == emailId);

            Assert.NotNull(instance);
            Assert.False(instance!.IsProjectBound); // Proposal is project-independent
            Assert.Equal(WorkflowStatus.Active, instance.Status);
        }
    }

    [Fact]
    public async Task Email_CreatePriceQuote_action_is_idempotent_per_email()
    {
        var (provider, options) = await BuildSeededProviderAsync();
        await using (provider)
        {
            var (_, emailId, defId) = await SeedProjectAndEmailAsync(options);
            var execution = BuildExecutionService(provider);

            var first = await execution.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    EmailSuggestedActionCodes.CreatePriceQuote, emailId, UserId),
                CancellationToken.None);
            Assert.True(first.Succeeded, first.Message);

            var second = await execution.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    EmailSuggestedActionCodes.CreatePriceQuote, emailId, UserId),
                CancellationToken.None);

            Assert.False(second.Succeeded);
            Assert.Contains("כבר קיים", second.Message);

            await using var db = new SiNetSQLDbContext(options);
            var count = await db.WorkflowInstances.CountAsync(w =>
                w.WorkflowDefinitionId == defId && w.TriggerEntityId == emailId);
            Assert.Equal(1, count);
        }
    }

    private static SqlEmailSuggestedActionExecutionService BuildExecutionService(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IProcessActionService>(),
            provider.GetRequiredService<IWorkflowCommandService>(),
            provider.GetRequiredService<IWorkflowQueryService>(),
            provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
            provider.GetService<ITaskCompletionService>());

    // ────────────────────────────────────────────────────────────────────────
    // Harness
    // ────────────────────────────────────────────────────────────────────────

    private static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, DbContextOptions<SiNetSQLDbContext> Options)> BuildSeededProviderAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new StubDbContextFactory(options);

        await SeedLookupsAsync(factory);
        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSiNetProcessBackbone();
        return (services.BuildServiceProvider(), options);
    }

    private static async Task SeedLookupsAsync(IDbContextFactory<SiNetSQLDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.ProjectStatuses.AddRange(
            new ProjectStatus { Code = ProjectStatusCodes.LeadReceived, Title = "ליד התקבל", IsActive = true, SortOrder = 10 },
            new ProjectStatus { Code = ProjectStatusCodes.QuotePreparation, Title = "הכנת הצעה", IsActive = true, SortOrder = 20 },
            new ProjectStatus { Code = ProjectStatusCodes.Active, Title = "פעיל", IsActive = true, SortOrder = 50 },
            new ProjectStatus { Code = ProjectStatusCodes.WaitingForMaterial, Title = "ממתין לחומר", IsActive = true, SortOrder = 55 },
            new ProjectStatus { Code = ProjectStatusCodes.WaitingForQuoteApproval, Title = "ממתין לאישור", IsActive = true, SortOrder = 60 },
            new ProjectStatus { Code = ProjectStatusCodes.WaitingForWorkOrder, Title = "ממתין להזמנה", IsActive = true, SortOrder = 70 },
            new ProjectStatus { Code = ProjectStatusCodes.ClosedLost, Title = "נסגר", IsActive = true, SortOrder = 90 });

        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "פתוח", IsActive = true, IsOpen = true, IsActionable = true, SortOrder = 10 },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "הושלם", IsActive = true, IsOpen = false, IsActionable = false, SortOrder = 60 });

        db.TaskTypes.AddRange(
            new TaskType { Code = TaskTypeCodes.IdentifyQuoteRequest, Name = "זיהוי בקשה", IsActive = true },
            new TaskType { Code = TaskTypeCodes.OpenQuoteProject, Name = "פתיחת פרויקט", IsActive = true },
            new TaskType { Code = TaskTypeCodes.FileQuoteMaterial, Name = "תיוק חומר", IsActive = true },
            new TaskType { Code = TaskTypeCodes.CheckQuoteMaterialCompleteness, Name = "בדיקת שלמות", IsActive = true },
            new TaskType { Code = TaskTypeCodes.PrepareQuoteCalculation, Name = "תחשיב", IsActive = true },
            new TaskType { Code = TaskTypeCodes.PrepareQuoteDocument, Name = "מסמך הצעה", IsActive = true },
            new TaskType { Code = TaskTypeCodes.ApproveQuoteInternal, Name = "אישור פנימי", IsActive = true },
            new TaskType { Code = TaskTypeCodes.FollowQuoteApproval, Name = "מעקב אישור", IsActive = true });

        db.TaskResultDefinitions.AddRange(
            new TaskResultDefinition { Code = TaskResultCodes.QuoteRequestDetected, Name = "זוהתה בקשה", Category = "Proposal", IsActive = true, SortOrder = 10 },
            new TaskResultDefinition { Code = TaskResultCodes.NotQuoteRequest, Name = "לא בקשה", Category = "Proposal", IsActive = true, SortOrder = 20 },
            new TaskResultDefinition { Code = TaskResultCodes.ProjectOpened, Name = "פרויקט נפתח", Category = "Project", IsActive = true, SortOrder = 30 },
            new TaskResultDefinition { Code = TaskResultCodes.MaterialComplete, Name = "חומר מלא", Category = "Proposal", IsActive = true, SortOrder = 40 },
            new TaskResultDefinition { Code = TaskResultCodes.MaterialMissing, Name = "חומר חסר", Category = "Proposal", IsActive = true, SortOrder = 50 });

        var user = new Siuser { Id = UserId, Name = "Test User", IsActive = true };
        db.Siusers.Add(user);
        await db.SaveChangesAsync();

        foreach (var code in new[] { UserGroupCodes.OfficeManagement, UserGroupCodes.SeniorManagement, UserGroupCodes.Planners })
        {
            var group = new UserGroup { Code = code, Name = code, IsActive = true, DefaultAssigneeId = UserId };
            db.UserGroups.Add(group);
            await db.SaveChangesAsync();
            db.UserGroupMemberships.Add(new UserGroupMembership { SiuserId = UserId, UserGroupId = group.Id });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<(int ProjectId, int EmailId, int DefId)> SeedProjectAndEmailAsync(
        DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);

        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "Native E2E", ProjectStatusId = active.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var email = new EmailInboxMessage
        {
            MessageUniqueId = $"<msg-{Guid.NewGuid():N}@test>",
            ProjectId = project.Id,
            FromAddress = "client@example.com",
            Subject = "Quote please",
        };
        db.EmailInboxMessages.Add(email);
        await db.SaveChangesAsync();

        var defId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => d.Id)
            .FirstAsync();

        return (project.Id, email.Id, defId);
    }

    private static async Task<ProjectAssignment> GetOpenStageTaskAsync(
        DbContextOptions<SiNetSQLDbContext> options, int instanceId, string taskTypeCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        return await db.ProjectAssignments
            .Include(t => t.TaskType)
            .Include(t => t.TaskLinks)
            .Include(t => t.AssignmentStatus)
            .Where(t => t.TaskType!.Code == taskTypeCode
                     && t.TaskLinks.Any(l =>
                            l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                         && l.LinkedEntityId == instanceId))
            .OrderByDescending(t => t.Id)
            .FirstAsync();
    }

    private static async Task MarkTaskCompletedAsync(
        DbContextOptions<SiNetSQLDbContext> options, int taskId, string resultCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        var completed = await db.ProjectAssignmentStatuses.FirstAsync(s => s.Code == TaskStatusCodes.Completed);
        var result = await db.TaskResultDefinitions.FirstAsync(r => r.Code == resultCode);
        var task = await db.ProjectAssignments.FirstAsync(t => t.Id == taskId);
        task.StatusId = completed.Id;
        task.Status = completed.Code;
        task.LastTaskResultId = result.Id;
        await db.SaveChangesAsync();
    }

    private static async Task AssertCurrentStageAsync(
        DbContextOptions<SiNetSQLDbContext> options, int instanceId, string expectedStageCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        var instance = await db.WorkflowInstances
            .Include(i => i.CurrentStage)
            .FirstAsync(i => i.Id == instanceId);
        Assert.Equal(expectedStageCode, instance.CurrentStage!.Code);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
