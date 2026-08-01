using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;
using DomainWorkflowStatus = SiNet.Domain.Workflow.WorkflowStatus;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class ProjectTypeContinuationStarterTests
{
    [Fact]
    public async Task Validate_when_project_type_missing_mapping_then_fails()
    {
        var (factory, projectId) = await BuildAsync(mapTypes: false);

        var starter = new SqlProjectTypeContinuationStarter(factory, new SpyWorkflowCommands());
        var result = await starter.ValidateMappingsAsync(projectId);

        Assert.False(result.Success);
        Assert.Contains("חסר מיפוי", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_when_two_types_share_definition_then_starts_twice_with_job_types()
    {
        var (factory, projectId) = await BuildAsync(mapTypes: true, twoTypesSameDefinition: true);
        var spy = new SpyWorkflowCommands();
        var starter = new SqlProjectTypeContinuationStarter(factory, spy);

        var result = await starter.StartContinuationsAsync(projectId, actingUserId: 1);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, spy.StartedCommands.Count);
        Assert.All(spy.StartedCommands, c => Assert.Equal(10, c.DefinitionId));
        Assert.Equal(new[] { 1, 2 }, spy.StartedCommands.Select(c => c.JobTypeId!.Value).OrderBy(x => x));
        Assert.Equal(2, result.StartedInstanceIds.Count);
    }

    [Fact]
    public async Task Start_when_active_track_exists_then_skips_that_track_only()
    {
        var (factory, projectId) = await BuildAsync(
            mapTypes: true,
            twoTypesSameDefinition: true,
            seedActiveInstanceForJobTypeId: 1);
        var spy = new SpyWorkflowCommands();
        var starter = new SqlProjectTypeContinuationStarter(factory, spy);

        var result = await starter.StartContinuationsAsync(projectId, actingUserId: 1);

        Assert.True(result.Success, result.Error);
        Assert.Single(spy.StartedCommands);
        Assert.Equal(2, spy.StartedCommands[0].JobTypeId);
        Assert.Single(result.StartedInstanceIds);
        Assert.Contains("PlanningWorkflow", result.SkippedAlreadyActiveCodes[0], StringComparison.Ordinal);
    }

    private static async Task<(IDbContextFactory<SiNetSQLDbContext> Factory, int ProjectId)> BuildAsync(
        bool mapTypes,
        bool twoTypesSameDefinition = false,
        int? seedActiveInstanceForJobTypeId = null)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);

        await using var db = await factory.CreateDbContextAsync();
        db.Projects.Add(new Project { Id = 100, Title = "Cont Project" });
        db.JobTypes.AddRange(
            new JobType { Id = 1, Title = "תכנון" },
            new JobType { Id = 2, Title = "כבישים" });
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = 10,
            Code = "PlanningWorkflow",
            Name = "Planning",
            IsActive = true,
        });

        db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
        {
            ProjectId = 100,
            ProjectTypeId = 1,
        });

        if (twoTypesSameDefinition)
        {
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = 100,
                ProjectTypeId = 2,
            });
        }

        if (mapTypes)
        {
            db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
            {
                ProjectTypeId = 1,
                WorkflowDefinitionId = 10,
                IsDefault = true,
                IsEnabled = true,
                SortOrder = 1,
            });

            if (twoTypesSameDefinition)
            {
                db.ProjectTypeWorkflowDefinitions.Add(new ProjectTypeWorkflowDefinition
                {
                    ProjectTypeId = 2,
                    WorkflowDefinitionId = 10,
                    IsDefault = true,
                    IsEnabled = true,
                    SortOrder = 1,
                });
            }
        }

        if (seedActiveInstanceForJobTypeId is int jobTypeId)
        {
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = 55,
                ProjectId = 100,
                WorkflowDefinitionId = 10,
                JobTypeId = jobTypeId,
                IsProjectBound = true,
                Status = WorkflowStatus.Active,
                CreatedByUserId = 1,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return (factory, 100);
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class SpyWorkflowCommands : IWorkflowCommandService
    {
        public List<StartWorkflowCommand> StartedCommands { get; } = [];
        private int _nextInstanceId = 1000;

        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
        {
            StartedCommands.Add(command);
            var id = ++_nextInstanceId;
            var instance = new WorkflowInstanceDto(
                id,
                command.DefinitionId,
                command.ProjectId,
                DomainWorkflowStatus.Active,
                CurrentStageId: null,
                CreatedAtUtc: DateTime.UtcNow,
                CompletedAtUtc: null,
                Notes: command.Notes,
                WorkflowDefinition: null,
                CurrentStage: null,
                Project: null,
                CreatedByUser: null,
                StageTransitions: [],
                JobTypeId: command.JobTypeId);
            return ValueTask.FromResult(new WorkflowStartResultDto(instance, []));
        }

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
