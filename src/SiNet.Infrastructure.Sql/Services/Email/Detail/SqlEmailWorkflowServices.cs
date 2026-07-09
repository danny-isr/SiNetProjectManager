using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Application.Email.Detail;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailWorkflowContextService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailWorkflowContextService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailWorkflowContextDto?> AnalyzeAsync(
        EmailWorkflowContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var message = await db.EmailInboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return null;
        }

        var projectId = query.OverrideProjectId ?? message.ProjectId;
        if (projectId <= 0)
        {
            return new EmailWorkflowContextDto(
                HasContext: false,
                null,
                null,
                null,
                ActiveWorkflowCount: 0,
                AttachmentCount: 0);
        }

        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return null;
        }

        var activeWorkflows = await db.WorkflowInstances
            .AsNoTracking()
            .CountAsync(
                w => w.ProjectId == projectId
                     && w.Status != WorkflowStatus.Completed
                     && w.Status != WorkflowStatus.Cancelled,
                cancellationToken)
            .ConfigureAwait(false);

        var attachmentCount = await db.EmailInboxAttachments
            .AsNoTracking()
            .CountAsync(a => a.MessageId == inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        var projectDisplay = !string.IsNullOrWhiteSpace(project.NameAndNumber)
            ? project.NameAndNumber
            : !string.IsNullOrWhiteSpace(project.Title)
                ? project.Title
                : $"Project #{project.Id}";

        return new EmailWorkflowContextDto(
            HasContext: true,
            projectDisplay,
            WorkflowFamilyDisplay: null,
            activeWorkflows > 0 ? "גבוהה" : "בינונית",
            activeWorkflows,
            attachmentCount);
    }
}

internal sealed class SqlEmailSuggestedActionService : IEmailSuggestedActionService
{
    public IReadOnlyList<EmailSuggestedActionDto> BuildActions(EmailWorkflowContextDto context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.HasContext)
        {
            return Array.Empty<EmailSuggestedActionDto>();
        }

        var actions = new List<EmailSuggestedActionDto>();

        if (context.AttachmentCount > 0)
        {
            actions.Add(new EmailSuggestedActionDto(
                ProcessActionCodes.SendNotification,
                "שלח התראה",
                "הודעה לצוות על מייל עם קבצים מצורפים",
                SortOrder: 10));
        }

        if (context.ActiveWorkflowCount > 0)
        {
            actions.Add(new EmailSuggestedActionDto(
                ProcessActionCodes.RecordTaskResult,
                "רשום תוצאת משימה",
                "עדכון סטטוס משימה קשורה",
                SortOrder: 20));
        }

        actions.Add(new EmailSuggestedActionDto(
            ProcessActionCodes.SetProjectStatus,
            "עדכן סטטוס פרויקט",
            "שינוי סטטוס פרויקט לאחר טיפול במייל",
            SortOrder: 30));

        return actions;
    }

    public string? SelectedActionCode => null;
}

internal sealed class SqlEmailSuggestedActionExecutionService(IProcessActionService processActions)
    : IEmailSuggestedActionExecutionService
{
    private readonly IProcessActionService _processActions =
        processActions ?? throw new ArgumentNullException(nameof(processActions));

    public async Task<EmailSuggestedActionExecutionResult> ExecuteAsync(
        EmailSuggestedActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_processActions.HasHandler(command.ActionCode))
        {
            return new EmailSuggestedActionExecutionResult(
                false,
                RequiresFollowUp: false,
                $"Handler not registered for action '{command.ActionCode}'.");
        }

        var result = await _processActions
            .DispatchAsync(
                new ActionExecutionCommand(
                    command.ActionCode,
                    ProjectId: null,
                    UserId: command.ActingUserId,
                    Data: command.InboxMessageId is int inboxId
                        ? new Dictionary<string, object?> { ["InboxMessageId"] = inboxId }
                        : null),
                cancellationToken)
            .ConfigureAwait(false);

        return new EmailSuggestedActionExecutionResult(
            result.Status == ActionExecutionStatus.Completed,
            result.Status == ActionExecutionStatus.Deferred,
            result.Message);
    }
}
