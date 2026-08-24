using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Application.Actions;
using SiNet.Application.Email.Detail;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Email.Detail;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Proves the two email suggested-action defects from audit §2.2 before any product behaviour change.
/// <para>
/// When the product wiring is fixed, convert these tests to assert success and post-conditions instead
/// of expecting failure messages — do not leave characterization tests that encode the bug as the desired
/// behaviour.
/// </para>
/// </summary>
public sealed class EmailAssociatedProcessActionDefectTests
{
    private const int UserId = ProposalWorkflowHarness.UserId;

    [Fact]
    public async Task Email_SetProjectStatus_action_fails_without_workflow_context()
    {
        var (provider, options) = await BuildProviderAsync();
        await using (provider)
        {
            var (projectId, inboxId, _) = await SeedAssociatedEmailAsync(options);
            var execution = BuildExecutionService(provider);

            var result = await execution.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    ProcessActionCodes.SetProjectStatus,
                    inboxId,
                    UserId),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("ProjectStatusCode is required.", result.Message, StringComparison.Ordinal);

            await using var db = new SiNetSQLDbContext(options);
            var unchanged = await db.Projects.AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => p.ProjectStatusId)
                .SingleAsync();
            Assert.True(unchanged > 0);
        }
    }

    [Fact]
    public async Task Email_RecordTaskResult_action_fails_without_workflow_instance_id()
    {
        var (provider, options) = await BuildProviderAsync();
        await using (provider)
        {
            var (_, inboxId, instanceId) = await SeedAssociatedEmailWithActiveWorkflowAsync(options);
            var execution = BuildExecutionService(provider);

            var result = await execution.ExecuteAsync(
                new EmailSuggestedActionExecutionCommand(
                    ProcessActionCodes.RecordTaskResult,
                    inboxId,
                    UserId),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("WorkflowInstanceId is required.", result.Message, StringComparison.Ordinal);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances.AsNoTracking()
                .SingleAsync(i => i.Id == instanceId);
            Assert.Equal(WorkflowStatus.Active, instance.Status);
        }
    }

    private static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, DbContextOptions<SiNetSQLDbContext> Options)>
        BuildProviderAsync()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        return (provider, options);
    }

    private static SqlEmailSuggestedActionExecutionService BuildExecutionService(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IProcessActionService>(),
            provider.GetRequiredService<IWorkflowCommandService>(),
            provider.GetRequiredService<IWorkflowQueryService>(),
            provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
            provider.GetService<ITaskCompletionService>());

    private static async Task<(int ProjectId, int InboxId, int DefId)> SeedAssociatedEmailAsync(
        DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);

        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project
        {
            Title = "[SYS-CERT] associated email project",
            ProjectStatusId = active.Id,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var email = new EmailInboxMessage
        {
            MessageUniqueId = $"<assoc-{Guid.NewGuid():N}@test>",
            ProjectId = project.Id,
            FromAddress = "client@example.com",
            Subject = "Associated email",
        };
        db.EmailInboxMessages.Add(email);
        await db.SaveChangesAsync();

        var defId = await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => d.Id)
            .FirstAsync();

        return (project.Id, email.Id, defId);
    }

    private static async Task<(int ProjectId, int InboxId, int InstanceId)>
        SeedAssociatedEmailWithActiveWorkflowAsync(
            DbContextOptions<SiNetSQLDbContext> options)
    {
        var (projectId, inboxId, defId) = await SeedAssociatedEmailAsync(options);

        await using var db = new SiNetSQLDbContext(options);
        var stageId = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == defId && s.IsInitial)
            .Select(s => s.Id)
            .FirstAsync();

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = defId,
            ProjectId = projectId,
            IsProjectBound = true,
            Status = WorkflowStatus.Active,
            CurrentStageId = stageId,
            TriggerType = WorkflowTriggerType.Email,
            TriggerEntityId = inboxId,
            CreatedByUserId = UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        return (projectId, inboxId, instance.Id);
    }
}
