using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
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
