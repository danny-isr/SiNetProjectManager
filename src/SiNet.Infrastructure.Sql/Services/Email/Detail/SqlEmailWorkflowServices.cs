using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email.Detail;
using SiNet.Application.Settings;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
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

        // Inbox rows always have a ProjectId (defaults to office "ניהול משרד").
        // Associated = filed to a real project, not the office default.
        var defaultOfficeProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken)
            .ConfigureAwait(false);
        var projectId = message.ProjectId;
        var isAssociated = projectId > 0
                           && (defaultOfficeProjectId <= 0 || projectId != defaultOfficeProjectId);

        if (!isAssociated)
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

    private static async Task<int> ResolveDefaultOfficeProjectIdAsync(
        SiNetSQLDbContext db,
        CancellationToken cancellationToken)
    {
        var defaultTitle = await db.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.SettingKey == SystemSettingKeys.DefaultProjectTitle)
            .Select(setting => setting.SettingValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(defaultTitle))
        {
            defaultTitle = SystemSettingsDefaults.DefaultProjectTitle;
        }

        return await db.Projects
            .AsNoTracking()
            .Where(project => project.Title == defaultTitle)
            .Select(project => project.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class SqlEmailSuggestedActionService : IEmailSuggestedActionService
{
    public IReadOnlyList<EmailSuggestedActionDto> BuildActions(EmailWorkflowContextDto context) =>
        EmailSuggestedActionsBuilder.Build(context);

    public string? SelectedActionCode => null;
}

internal sealed class SqlEmailSuggestedActionExecutionService(
    IProcessActionService processActions,
    IWorkflowCommandService workflowCommands,
    IWorkflowQueryService workflowQuery,
    IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailSuggestedActionExecutionService
{
    private readonly IProcessActionService _processActions =
        processActions ?? throw new ArgumentNullException(nameof(processActions));
    private readonly IWorkflowCommandService _workflowCommands =
        workflowCommands ?? throw new ArgumentNullException(nameof(workflowCommands));
    private readonly IWorkflowQueryService _workflowQuery =
        workflowQuery ?? throw new ArgumentNullException(nameof(workflowQuery));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailSuggestedActionExecutionResult> ExecuteAsync(
        EmailSuggestedActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.Action",
            $"action={command.ActionCode} inbox={command.InboxMessageId?.ToString() ?? "(none)"} user={command.ActingUserId}");

        // Phase 3e: email-driven workflow starts that need no UI / project creation are routed
        // through the native IWorkflowCommandService.StartAsync (single native engine).
        if (TryResolveWorkflowStart(command.ActionCode, out var workflowCode, out var isProjectBound))
        {
            return await StartWorkflowAsync(command, workflowCode, isProjectBound, cancellationToken)
                .ConfigureAwait(false);
        }

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

    /// <summary>
    /// Maps email suggested-action codes to native workflow starts that require no UI / project
    /// creation. Mirrors the legacy <c>ActionExecutor</c> dispatch:
    /// <c>CreatePriceQuote → Proposal</c> (project-independent) and
    /// <c>CreateOpinionProject → Opinion</c> (bound to the email's office project).
    /// UI-/project-creation-dependent starts (CreateNewReview, RequestAuthorityInvitation, …)
    /// are deferred to the ProjectWork surface (Phase 5a).
    /// </summary>
    private static bool TryResolveWorkflowStart(string actionCode, out string workflowCode, out bool isProjectBound)
    {
        switch (actionCode)
        {
            case EmailSuggestedActionCodes.CreatePriceQuote:
                workflowCode = WorkflowCodes.Proposal;
                isProjectBound = false;
                return true;
            case EmailSuggestedActionCodes.CreateOpinionProject:
                workflowCode = WorkflowCodes.Opinion;
                isProjectBound = true;
                return true;
            default:
                workflowCode = string.Empty;
                isProjectBound = false;
                return false;
        }
    }

    private async Task<EmailSuggestedActionExecutionResult> StartWorkflowAsync(
        EmailSuggestedActionExecutionCommand command,
        string workflowCode,
        bool isProjectBound,
        CancellationToken cancellationToken)
    {
        if (command.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            return new EmailSuggestedActionExecutionResult(false, false, "חסר מזהה מייל להפעלת התהליך.");
        }

        var definitions = await _workflowQuery.GetActiveDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(
            d => string.Equals(d.Code, workflowCode, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return new EmailSuggestedActionExecutionResult(
                false, false, $"תבנית תהליך '{workflowCode}' לא נמצאה או אינה פעילה.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var message = await db.EmailInboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return new EmailSuggestedActionExecutionResult(false, false, "המייל לא נמצא.");
        }

        // Inbox rows always carry a ProjectId (office default when unassigned). It backs the
        // office placeholder for project-independent starts (Proposal) and the owning project
        // for bound starts (Opinion) — matching legacy ActionExecutor behaviour.
        var projectId = message.ProjectId;
        if (projectId <= 0)
        {
            return new EmailSuggestedActionExecutionResult(
                false, true, "לא נמצא פרויקט לשיוך התהליך — פתח דרך משטח העבודה.");
        }

        // Duplicate guard per source email (mirrors legacy EnsureNoDuplicateForEmailAsync).
        var existing = await db.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.WorkflowDefinitionId == definition.Id
                        && w.TriggerType == WorkflowTriggerType.Email
                        && w.TriggerEntityId == inboxMessageId
                        && w.Status != WorkflowStatus.Cancelled)
            .OrderByDescending(w => w.Id)
            .Select(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing > 0)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.StartWorkflow",
                $"inbox={inboxMessageId} workflow={workflowCode} DUPLICATE-GUARD hit (existing instance #{existing}) — not started");
            return new EmailSuggestedActionExecutionResult(
                false, false, $"כבר קיים תהליך '{definition.Name}' עבור מייל זה (#{existing}).");
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.StartWorkflow",
            $"inbox={inboxMessageId} workflow={workflowCode} def={definition.Id} project={projectId} bound={isProjectBound} → starting");

        try
        {
            var result = await _workflowCommands
                .StartAsync(
                    new StartWorkflowCommand(
                        definition.Id,
                        projectId,
                        WorkflowTriggerTypeDto.Email,
                        TriggerEntityId: inboxMessageId,
                        command.ActingUserId,
                        Notes: null,
                        IsProjectBound: isProjectBound),
                    cancellationToken)
                .ConfigureAwait(false);

            var message2 = result.CreatedTasks.Count > 0
                ? $"תהליך '{definition.Name}' הופעל בהצלחה ונוצרה משימה."
                : $"תהליך '{definition.Name}' הופעל (מופע #{result.Instance.Id}), אך לא נוצרו משימות לשלב הראשון.";

            return new EmailSuggestedActionExecutionResult(true, false, message2);
        }
        catch (Exception ex)
        {
            return new EmailSuggestedActionExecutionResult(
                false, false, $"שגיאה בהפעלת תהליך '{definition.Name}': {ex.Message}");
        }
    }
}
