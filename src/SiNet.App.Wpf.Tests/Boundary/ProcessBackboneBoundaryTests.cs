using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Application.WorkSurfaces;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.DependencyInjection;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNet.LegacyBridge.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Proves the native process backbone (Workflow reads + Task + Action ports) registers and runs
/// without LegacyBridge task seams.
/// </summary>
public sealed class ProcessBackboneBoundaryTests
{
    [Fact]
    public void New_process_backbone_services_are_registered_without_LegacyBridge()
    {
        var services = new ServiceCollection();
        services.AddSiNetProcessBackbone();

        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowQueryService));
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskNavigationService));
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskCompletionService));
        Assert.Contains(services, d => d.ServiceType == typeof(IProcessActionService));
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskCompletionMetadataResolver));
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskQueryService));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ILegacyTaskNavigationSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ILegacyTaskCompletionSource));
        Assert.DoesNotContain(
            services,
            d => d.ImplementationType?.FullName?.Contains("LegacyTaskNavigationService", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            services,
            d => d.ImplementationType?.FullName?.Contains("LegacyTaskCompletionService", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Task_navigation_service_returns_WorkSurfaceContext_from_database_or_test_db()
    {
        var (provider, taskId) = await CreateSeededProviderAsync(seedNavigation: true).ConfigureAwait(false);
        await using var scope = provider.CreateAsyncScope();

        var navigation = scope.ServiceProvider.GetRequiredService<ITaskNavigationService>();
        var context = await navigation.ResolveAsync(taskId, CancellationToken.None).ConfigureAwait(false);

        Assert.NotNull(context);
        Assert.Equal(taskId, context!.TaskId);
        Assert.Equal(TaskComponentKeys.InspectionReport, context.ComponentKey);
        Assert.Equal(TaskTypeCodes.PerformProfessionalReview, context.TaskTypeCode);
        Assert.Equal(42, context.PrimaryWorkTargetEntityId);
    }

    [Fact]
    public async Task Task_completion_service_closes_task_and_returns_result()
    {
        var (provider, taskId) = await CreateSeededProviderAsync(seedNavigation: false).ConfigureAwait(false);
        await using var scope = provider.CreateAsyncScope();

        var completion = scope.ServiceProvider.GetRequiredService<ITaskCompletionService>();
        var result = await completion.CompleteAsync(
            new CompleteTaskCommand(
                TaskId: taskId,
                CompletionEventCode: ReviewCompletionEvents.ReviewProfessionalReviewCompleted,
                TaskResultCode: TaskResultCodes.ProfessionalReviewCompleted,
                CompletedTaskLinkIds: null,
                UserId: 7),
            CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.True(result.TaskClosed);
        Assert.Equal(TaskResultCodes.ProfessionalReviewCompleted, result.RecordedTaskResultCode);

        await using var verifyScope = provider.CreateAsyncScope();
        var dbFactory = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var task = await db.ProjectAssignments.AsNoTracking().FirstAsync(t => t.Id == taskId).ConfigureAwait(false);
        var completed = await db.ProjectAssignmentStatuses.AsNoTracking()
            .FirstAsync(s => s.Code == TaskStatusCodes.Completed).ConfigureAwait(false);
        Assert.Equal(completed.Id, task.StatusId);
    }

    [Fact]
    public async Task Task_completion_invokes_workflow_command_service_when_policy_requires()
    {
        var fakeWorkflow = new RecordingWorkflowCommandService();
        var (provider, taskId) = await CreateSeededProviderAsync(
            seedNavigation: false,
            configureServices: s => s.AddSingleton<IWorkflowCommandService>(fakeWorkflow)).ConfigureAwait(false);

        await using var scope = provider.CreateAsyncScope();
        var completion = scope.ServiceProvider.GetRequiredService<ITaskCompletionService>();

        var result = await completion.CompleteAsync(
            new CompleteTaskCommand(
                TaskId: taskId,
                CompletionEventCode: ReviewCompletionEvents.ReviewProfessionalReviewCompleted,
                TaskResultCode: TaskResultCodes.ProfessionalReviewCompleted,
                CompletedTaskLinkIds: null,
                UserId: 7),
            CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.Success);
        Assert.True(result.WorkflowAdvanced);
        Assert.Equal(1, fakeWorkflow.AutoAdvanceCallCount);
        Assert.Equal(taskId, fakeWorkflow.LastTaskClosedCommand?.TaskId);
    }

    [Fact]
    public async Task Task_navigation_service_returns_null_when_multiple_work_targets()
    {
        var (provider, taskId) = await CreateSeededProviderAsync(
            seedNavigation: true,
            seedMultipleWorkTargets: true).ConfigureAwait(false);
        await using var scope = provider.CreateAsyncScope();

        var navigation = scope.ServiceProvider.GetRequiredService<ITaskNavigationService>();
        var context = await navigation.ResolveAsync(taskId, CancellationToken.None).ConfigureAwait(false);

        Assert.Null(context);
    }

    [Fact]
    public async Task Task_completion_reports_failure_when_workflow_auto_advance_throws()
    {
        var throwingWorkflow = new ThrowingWorkflowCommandService();
        var (provider, taskId) = await CreateSeededProviderAsync(
            seedNavigation: false,
            configureServices: s => s.AddSingleton<IWorkflowCommandService>(throwingWorkflow)).ConfigureAwait(false);

        await using var scope = provider.CreateAsyncScope();
        var completion = scope.ServiceProvider.GetRequiredService<ITaskCompletionService>();

        var result = await completion.CompleteAsync(
            new CompleteTaskCommand(
                TaskId: taskId,
                CompletionEventCode: ReviewCompletionEvents.ReviewProfessionalReviewCompleted,
                TaskResultCode: TaskResultCodes.ProfessionalReviewCompleted,
                CompletedTaskLinkIds: null,
                UserId: 7),
            CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.Success);
        Assert.True(result.TaskClosed);
        Assert.True(result.WorkflowAdvanced);
        Assert.Contains("Workflow auto-advance failed", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_action_dispatcher_executes_registered_handler()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(dbName).Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetActionServices();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var actions = provider.GetRequiredService<IProcessActionService>();
        Assert.True(actions.HasHandler(ProcessActionCodes.SendNotification));

        var result = await actions.DispatchAsync(
            new ActionExecutionCommand(ProcessActionCodes.SendNotification, ProjectId: 1, UserId: 2),
            CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(ProcessActionCodes.SendNotification, result.ActionCode);
        Assert.Equal(ActionExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public void ProcessBackbone_readiness_matrix_is_documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "PROCESS_BACKBONE_FOUNDATION.md"));
        Assert.Contains("Level 0", doc, StringComparison.Ordinal);
        Assert.Contains("Level 5", doc, StringComparison.Ordinal);
        Assert.Contains("Work Surface readiness matrix", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email read-only", doc, StringComparison.Ordinal);
        Assert.Contains("Email filing", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_query_service_is_not_empty_or_is_explicitly_deferred()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "SiNet.Application", "Tasks", "ITaskQueryService.cs"));
        Assert.Contains("GetByIdAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetTasksForProjectAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Intentionally empty", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_system_resolves_task_services_without_LegacyBridge()
    {
        var services = new ServiceCollection();
        services.AddSiNetProcessBackbone();
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskQueryService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ILegacyTaskNavigationSource));
    }

    [Fact]
    public void New_system_resolves_action_service_without_SiNetSQL_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(
            _ => new StubDbContextFactory(
                new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        services.AddSiNetActionServices();

        Assert.Contains(services, d => d.ServiceType == typeof(IProcessActionService));
        Assert.Contains(services, d => d.ImplementationType == typeof(SiNet.Infrastructure.Sql.Services.Actions.ProcessActionService));
        Assert.DoesNotContain(
            services,
            d => d.ImplementationType?.FullName?.Contains("SiNetSQL.Domain.Actions.ProcessActionDispatcher", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Minimal_action_handlers_execute_through_application_contract()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(dbName).Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.ProjectStatuses.Add(new ProjectStatus { Id = 1, Code = ProjectStatusCodes.Active, Title = "Active", IsActive = true });
            seed.Projects.Add(new Project { Id = 10, NameAndNumber = "P-10" });
            seed.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = 50,
                ProjectId = 10,
                IsProjectBound = true,
                Status = WorkflowStatus.Active,
            });
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetActionServices();
        await using var provider = services.BuildServiceProvider();

        var actions = provider.GetRequiredService<IProcessActionService>();

        var setStatus = await actions.DispatchAsync(
            new ActionExecutionCommand(
                ProcessActionCodes.SetProjectStatus,
                WorkflowInstanceId: 50,
                UserId: 1,
                Data: new Dictionary<string, object?> { [ActionExecutionDataKeys.ProjectStatusCode] = ProjectStatusCodes.Active }),
            CancellationToken.None);

        Assert.Equal(ActionExecutionStatus.Completed, setStatus.Status);

        var unknown = await actions.DispatchAsync(
            new ActionExecutionCommand("MoveToProject", ProjectId: 10),
            CancellationToken.None);
        Assert.Equal(ActionExecutionStatus.NotSupported, unknown.Status);
    }

    [Fact]
    public void Work_surface_readiness_blocks_Email_filing_until_MoveToProject_and_ACC_write_policy()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "PROCESS_BACKBONE_FOUNDATION.md"));
        Assert.Contains("Email filing", doc, StringComparison.Ordinal);
        Assert.Contains("Blocked", doc, StringComparison.Ordinal);
        Assert.Contains("MoveToProject", doc, StringComparison.Ordinal);
        Assert.Contains("ACC write", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Work_surface_readiness_allows_read_only_surfaces_when_no_write_foundation_needed()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "PROCESS_BACKBONE_FOUNDATION.md"));
        Assert.Contains("Email read-only", doc, StringComparison.Ordinal);
        Assert.Contains("Can start", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Task_query_service_returns_project_tasks_from_test_db()
    {
        var (provider, taskId) = await CreateSeededProviderAsync(seedNavigation: false);
        await using var scope = provider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ITaskQueryService>();

        var byId = await query.GetByIdAsync(taskId, CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal(TaskTypeCodes.PerformProfessionalReview, byId!.TaskTypeCode);
        Assert.Equal(TaskComponentKeys.InspectionReport, byId.ComponentKey);

        var forProject = await query.GetTasksForProjectAsync(5, includeClosed: false, ct: CancellationToken.None);
        Assert.Single(forProject);
        Assert.Equal(taskId, forProject[0].TaskId);
    }

    [Theory]
    [MemberData(nameof(NativeSurfaceSourceFiles))]
    public void New_System_WPF_has_no_direct_SiNetSQL_task_workflow_action_dependency(string relativePath)
    {
        if (relativePath.Contains("Surfaces/Email", StringComparison.OrdinalIgnoreCase))
            return;

        var forbidden =
            new[]
            {
                "using SiNetSQL",
                "TaskNavigationResolver",
                "TaskCompletionCoordinator",
                "ProcessActionDispatcher",
                "ILegacyTaskNavigationSource",
                "ILegacyTaskCompletionSource",
            };

        var content = File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var token in forbidden)
        {
            Assert.False(
                content.Contains(token, StringComparison.Ordinal),
                $"Forbidden legacy identifier '{token}' in {relativePath}");
        }
    }

    public static IEnumerable<object[]> NativeSurfaceSourceFiles()
        => WorkSurfaceWorkflowIntegrationBoundaryTests.NativeSurfaceSourceFiles();

    private static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, int TaskId)> CreateSeededProviderAsync(
        bool seedNavigation,
        bool seedMultipleWorkTargets = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            var openStatus = new ProjectAssignmentStatus { Id = 1, Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true };
            var completedStatus = new ProjectAssignmentStatus { Id = 2, Code = TaskStatusCodes.Completed, Name = "Completed", IsOpen = false };
            var taskType = new TaskType { Id = 1, Code = TaskTypeCodes.PerformProfessionalReview, Name = "Review" };
            var resultDef = new TaskResultDefinition
            {
                Id = 10,
                Code = TaskResultCodes.ProfessionalReviewCompleted,
                Name = "Done",
            };

            seed.ProjectAssignmentStatuses.AddRange(openStatus, completedStatus);
            seed.TaskTypes.Add(taskType);
            seed.TaskResultDefinitions.Add(resultDef);

            var task = new ProjectAssignment
            {
                Id = 100,
                ProjectId = 5,
                TaskTypeId = taskType.Id,
                StatusId = openStatus.Id,
                AssignedToId = 1,
                Title = "Perform review",
            };

            if (seedNavigation)
            {
                task.TaskLinks.Add(new TaskLink
                {
                    Id = 200,
                    TaskId = task.Id,
                    Role = TaskLinkRole.Related,
                    LinkedEntityType = TaskLinkEntityType.InspectionReport,
                    LinkedEntityId = 42,
                    IsWorkTarget = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = 1,
                });

                if (seedMultipleWorkTargets)
                {
                    task.TaskLinks.Add(new TaskLink
                    {
                        Id = 201,
                        TaskId = task.Id,
                        Role = TaskLinkRole.Related,
                        LinkedEntityType = TaskLinkEntityType.InspectionReport,
                        LinkedEntityId = 43,
                        IsWorkTarget = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedByUserId = 1,
                    });
                }
            }

            seed.ProjectAssignments.Add(task);
            await seed.SaveChangesAsync().ConfigureAwait(false);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetTaskServices();
        configureServices?.Invoke(services);
        if (!services.Any(d => d.ServiceType == typeof(IWorkflowCommandService)))
            services.AddSingleton<IWorkflowCommandService>(new NoOpWorkflowCommandService());

        return (services.BuildServiceProvider(), 100);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                    || File.Exists(Path.Combine(dir.FullName, "docs", "MIGRATION_MAP.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }

    private sealed class NoOpWorkflowCommandService : IWorkflowCommandService
    {
        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
            => ValueTask.FromResult<StageCompletionResultDto?>(null);

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingWorkflowCommandService : IWorkflowCommandService
    {
        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
            => throw new InvalidOperationException("simulated orchestrator failure");

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class RecordingWorkflowCommandService : IWorkflowCommandService
    {
        public int AutoAdvanceCallCount { get; private set; }
        public TaskClosedCommand? LastTaskClosedCommand { get; private set; }

        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
        {
            AutoAdvanceCallCount++;
            LastTaskClosedCommand = command;
            return ValueTask.FromResult<StageCompletionResultDto?>(
                new StageCompletionResultDto(
                    InstanceId: 1,
                    CompletedStageId: 2,
                    Action: StageCompletionActionDto.AutoAdvanced));
        }

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
