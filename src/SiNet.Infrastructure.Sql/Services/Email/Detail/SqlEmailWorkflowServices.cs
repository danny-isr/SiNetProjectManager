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

        var attachmentCount = await db.EmailInboxAttachments
            .AsNoTracking()
            .CountAsync(a => a.MessageId == inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        var projectId = query.OverrideProjectId ?? message.ProjectId;
        if (projectId <= 0)
        {
            return new EmailWorkflowContextDto(
                HasContext: true,
                ProjectDisplay: "לא משויך לפרויקט",
                WorkflowFamilyDisplay: null,
                ConfidenceDisplay: null,
                ActiveWorkflowCount: 0,
                AttachmentCount: attachmentCount,
                IsAssociatedToProject: false);
        }

        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return new EmailWorkflowContextDto(
                HasContext: true,
                ProjectDisplay: "לא משויך לפרויקט",
                WorkflowFamilyDisplay: null,
                ConfidenceDisplay: null,
                ActiveWorkflowCount: 0,
                AttachmentCount: attachmentCount,
                IsAssociatedToProject: false);
        }

        var activeWorkflows = await db.WorkflowInstances
            .AsNoTracking()
            .CountAsync(
                w => w.ProjectId == projectId
                     && w.Status != WorkflowStatus.Completed
                     && w.Status != WorkflowStatus.Cancelled,
                cancellationToken)
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
            attachmentCount,
            IsAssociatedToProject: true);
    }
}

internal sealed class SqlEmailSuggestedActionService : IEmailSuggestedActionService
{
    public IReadOnlyList<EmailSuggestedActionDto> BuildActions(EmailWorkflowContextDto context) =>
        EmailSuggestedActionsBuilder.Build(context);

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

        if (IsUnassignedInboxAction(command.ActionCode))
        {
            return ResolveUnassignedAction(command.ActionCode);
        }

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

    private static bool IsUnassignedInboxAction(string actionCode) =>
        actionCode is EmailSuggestedActionCodes.AssociateToExistingProject
            or EmailSuggestedActionCodes.CreatePriceQuote
            or EmailSuggestedActionCodes.CreateNewReview
            or EmailSuggestedActionCodes.RequestAuthorityInvitation
            or EmailSuggestedActionCodes.CreateOpinionProject
            or EmailSuggestedActionCodes.CollectMaterial
            or EmailSuggestedActionCodes.ForwardToDecision
            or EmailSuggestedActionCodes.FileOnly;

    private static EmailSuggestedActionExecutionResult ResolveUnassignedAction(string actionCode) =>
        actionCode switch
        {
            EmailSuggestedActionCodes.AssociateToExistingProject
                or EmailSuggestedActionCodes.FileOnly =>
                new EmailSuggestedActionExecutionResult(
                    Succeeded: true,
                    RequiresFollowUp: true,
                    "השתמש בכפתור 'שייך לפרויקט' בסרגל הפעולות למעלה."),
            _ =>
                new EmailSuggestedActionExecutionResult(
                    Succeeded: false,
                    RequiresFollowUp: true,
                    "הפעולה עדיין לא מחוברת במערכת החדשה — בקרוב."),
        };
}
