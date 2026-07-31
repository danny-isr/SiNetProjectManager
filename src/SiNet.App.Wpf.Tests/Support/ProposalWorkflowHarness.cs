using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Support;

/// <summary>
/// Shared in-memory harness that seeds the real Proposal (PRP.*) workflow through
/// <see cref="SqlWorkflowSeedService"/> and wires the native process backbone. Reused by workflow
/// integrity / task-workbench tests that need a genuine <c>IWorkflowCommandService</c> whose
/// Pause/Resume actually flip <see cref="WorkflowInstance.Status"/>.
/// </summary>
public static class ProposalWorkflowHarness
{
    public const int UserId = 1;

    public static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, DbContextOptions<SiNetSQLDbContext> Options)> BuildSeededProviderAsync()
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
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "הושלם", IsActive = true, IsOpen = false, IsActionable = false, SortOrder = 60 },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Cancelled, Name = "בוטל", IsActive = true, IsOpen = false, IsActionable = false, SortOrder = 70 });

        db.TaskTypes.AddRange(
            new TaskType { Code = TaskTypeCodes.IdentifyQuoteRequest, Name = "זיהוי בקשה", IsActive = true },
            new TaskType { Code = TaskTypeCodes.OpenQuoteProject, Name = "פתיחת פרויקט", IsActive = true },
            new TaskType { Code = TaskTypeCodes.FileQuoteMaterial, Name = "תיוק חומר", IsActive = true },
            new TaskType { Code = TaskTypeCodes.CheckQuoteMaterialCompleteness, Name = "בדיקת שלמות", IsActive = true },
            new TaskType { Code = TaskTypeCodes.PrepareQuoteCalculation, Name = "תחשיב", IsActive = true },
            new TaskType { Code = TaskTypeCodes.PrepareQuoteDocument, Name = "מסמך הצעה", IsActive = true },
            new TaskType { Code = TaskTypeCodes.ApproveQuoteInternal, Name = "אישור פנימי", IsActive = true },
            new TaskType { Code = TaskTypeCodes.SendQuoteToClient, Name = "שליחת הצעה", IsActive = true },
            new TaskType { Code = TaskTypeCodes.FollowQuoteApproval, Name = "מעקב אישור", IsActive = true });

        db.TaskResultDefinitions.AddRange(
            new TaskResultDefinition { Code = TaskResultCodes.QuoteRequestDetected, Name = "זוהתה בקשה", Category = "Proposal", IsActive = true, SortOrder = 10 },
            new TaskResultDefinition { Code = TaskResultCodes.NotQuoteRequest, Name = "לא בקשה", Category = "Proposal", IsActive = true, SortOrder = 20 },
            new TaskResultDefinition { Code = TaskResultCodes.ProjectOpened, Name = "פרויקט נפתח", Category = "Project", IsActive = true, SortOrder = 30 },
            new TaskResultDefinition { Code = TaskResultCodes.MaterialComplete, Name = "חומר מלא", Category = "Proposal", IsActive = true, SortOrder = 40 },
            new TaskResultDefinition { Code = TaskResultCodes.MaterialMissing, Name = "חומר חסר", Category = "Proposal", IsActive = true, SortOrder = 50 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteCalculationCompleted, Name = "תחשיב הושלם", Category = "Quote", IsActive = true, SortOrder = 200 },
            new TaskResultDefinition { Code = TaskResultCodes.QuotePrepared, Name = "הצעה מוכנה", Category = "Quote", IsActive = true, SortOrder = 210 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteApprovedInternally, Name = "אושרה פנימית", Category = "Quote", IsActive = true, SortOrder = 220 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteRequiresRevision, Name = "דורשת תיקון", Category = "Quote", IsActive = true, SortOrder = 230 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteSent, Name = "נשלחה", Category = "Quote", IsActive = true, SortOrder = 240 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteApprovedByClient, Name = "אושרה לקוח", Category = "Quote", IsActive = true, SortOrder = 250 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteRejectedByClient, Name = "נדחתה לקוח", Category = "Quote", IsActive = true, SortOrder = 260 },
            new TaskResultDefinition { Code = TaskResultCodes.QuoteCancelledNoResponse, Name = "בוטל אין תגובה", Category = "Quote", IsActive = true, SortOrder = 270 });

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

    public static async Task<(int ProjectId, int EmailId, int DefId)> SeedProjectAndEmailAsync(
        DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);

        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "Integrity E2E", ProjectStatusId = active.Id };
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

    public static async Task<ProjectAssignment> GetOpenStageTaskAsync(
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

    public static async Task MarkTaskResultAsync(
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

    public sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
