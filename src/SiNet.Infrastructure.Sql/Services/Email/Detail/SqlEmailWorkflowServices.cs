using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email;
using SiNet.Application.Email.Detail;
using SiNet.Application.Settings;
using SiNet.Application.Tasks;
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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var message = await ResolveInboxMessageAsync(db, query, cancellationToken).ConfigureAwait(false);
        if (message is null)
        {
            // Gmail-only row (not materialized yet) — still offer unassigned actions.
            if (!string.IsNullOrWhiteSpace(query.GmailMessageId))
            {
                return new EmailWorkflowContextDto(
                    HasContext: true,
                    ProjectDisplay: "לא משויך לפרויקט",
                    WorkflowFamilyDisplay: null,
                    ConfidenceDisplay: null,
                    ActiveWorkflowCount: 0,
                    AttachmentCount: 0,
                    IsAssociatedToProject: false);
            }

            return null;
        }

        var inboxMessageId = message.Id;
        var attachmentCount = await db.EmailInboxAttachments
            .AsNoTracking()
            .CountAsync(a => a.MessageId == inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        var (hasProposal, proposalSummary) = await TryGetActiveProposalForEmailAsync(
                db, inboxMessageId, cancellationToken)
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
                WorkflowFamilyDisplay: hasProposal ? "הצעת מחיר" : null,
                ConfidenceDisplay: hasProposal ? "פעיל" : null,
                ActiveWorkflowCount: hasProposal ? 1 : 0,
                AttachmentCount: attachmentCount,
                IsAssociatedToProject: false,
                HasActiveProposalForEmail: hasProposal,
                ActiveProposalSummary: proposalSummary);
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
                WorkflowFamilyDisplay: hasProposal ? "הצעת מחיר" : null,
                ConfidenceDisplay: hasProposal ? "פעיל" : null,
                ActiveWorkflowCount: hasProposal ? 1 : 0,
                AttachmentCount: attachmentCount,
                IsAssociatedToProject: false,
                HasActiveProposalForEmail: hasProposal,
                ActiveProposalSummary: proposalSummary);
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
            WorkflowFamilyDisplay: hasProposal ? "הצעת מחיר" : null,
            activeWorkflows > 0 || hasProposal ? "גבוהה" : "בינונית",
            Math.Max(activeWorkflows, hasProposal ? 1 : 0),
            attachmentCount,
            IsAssociatedToProject: true,
            HasActiveProposalForEmail: hasProposal,
            ActiveProposalSummary: proposalSummary);
    }

    private static async Task<EmailInboxMessage?> ResolveInboxMessageAsync(
        SiNetSQLDbContext db,
        EmailWorkflowContextQuery query,
        CancellationToken cancellationToken)
    {
        if (query.InboxMessageId is int inboxMessageId && inboxMessageId > 0)
        {
            return await db.EmailInboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(query.InternetMessageId))
        {
            var cleaned = query.InternetMessageId.Trim().Trim('<', '>').Trim();
            if (!string.IsNullOrEmpty(cleaned))
            {
                var byRfc = await db.EmailInboxMessages
                    .AsNoTracking()
                    .Where(m => m.InternetMessageId == cleaned || m.MessageUniqueId == cleaned)
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (byRfc is not null)
                    return byRfc;
            }
        }

        if (string.IsNullOrWhiteSpace(query.GmailMessageId))
            return null;

        var gmailKey = $"gmail:{query.GmailMessageId}";
        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.MessageUniqueId == query.GmailMessageId || m.MessageUniqueId == gmailKey)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(bool HasProposal, string? Summary)> TryGetActiveProposalForEmailAsync(
        SiNetSQLDbContext db,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        var proposalDefId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (proposalDefId is null)
            return (false, null);

        var row = await db.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.WorkflowDefinitionId == proposalDefId.Value
                        && w.TriggerType == WorkflowTriggerType.Email
                        && w.TriggerEntityId == inboxMessageId
                        && w.Status != WorkflowStatus.Completed
                        && w.Status != WorkflowStatus.Cancelled)
            .OrderByDescending(w => w.Id)
            .Select(w => new
            {
                w.Id,
                StageName = w.CurrentStage != null ? w.CurrentStage.Name : null,
                StageCode = w.CurrentStage != null ? w.CurrentStage.Code : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return (false, null);

        var stage = !string.IsNullOrWhiteSpace(row.StageName)
            ? row.StageName
            : row.StageCode ?? "פעיל";
        return (true, $"נפתח תהליך הצעת מחיר #{row.Id} — {stage}");
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
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    ITaskCompletionService? taskCompletion = null)
    : IEmailSuggestedActionExecutionService
{
    private const string QuoteRequestClassifiedEvent = "Review.QuoteRequestClassified";
    private const string QuoteRequestDetected = "QuoteRequestDetected";
    private const string NotQuoteRequest = "NotQuoteRequest";

    private readonly IProcessActionService _processActions =
        processActions ?? throw new ArgumentNullException(nameof(processActions));
    private readonly IWorkflowCommandService _workflowCommands =
        workflowCommands ?? throw new ArgumentNullException(nameof(workflowCommands));
    private readonly IWorkflowQueryService _workflowQuery =
        workflowQuery ?? throw new ArgumentNullException(nameof(workflowQuery));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITaskCompletionService? _taskCompletion = taskCompletion;

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
        // CreatePriceQuote / RejectPriceQuote already express the intake classification — auto-complete
        // IdentifyQuoteRequest so the operator is not asked again in a second dialog.
        if (TryResolveWorkflowStart(command.ActionCode, out var workflowCode, out var isProjectBound, out var intakeResultCode, out var initialStageCode))
        {
            return await StartWorkflowAsync(command, workflowCode, isProjectBound, intakeResultCode, initialStageCode, cancellationToken)
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
            or EmailSuggestedActionCodes.RejectPriceQuote
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
    /// <c>CreatePriceQuote → Proposal</c> at ProjectSetup (click already means quote — skip Intake),
    /// <c>RejectPriceQuote → Proposal</c> (intake auto-classified as not-a-quote → terminal),
    /// <c>CreateOpinionProject → Opinion</c> (bound to the email's office project),
    /// <c>CreateNewReview → Review</c> at ProjectSetup (official request already received — skip Intake /
    /// AwaitingMunicipalityRequest).
    /// </summary>
    private static bool TryResolveWorkflowStart(
        string actionCode,
        out string workflowCode,
        out bool isProjectBound,
        out string? intakeResultCode,
        out string? initialStageCode)
    {
        switch (actionCode)
        {
            case EmailSuggestedActionCodes.CreatePriceQuote:
                workflowCode = WorkflowCodes.Proposal;
                isProjectBound = false;
                intakeResultCode = null;
                initialStageCode = ProposalStageCodes.ProjectSetup;
                return true;
            case EmailSuggestedActionCodes.RejectPriceQuote:
                workflowCode = WorkflowCodes.Proposal;
                isProjectBound = false;
                intakeResultCode = NotQuoteRequest;
                initialStageCode = null;
                return true;
            case EmailSuggestedActionCodes.CreateOpinionProject:
                workflowCode = WorkflowCodes.Opinion;
                isProjectBound = true;
                intakeResultCode = null;
                initialStageCode = null;
                return true;
            case EmailSuggestedActionCodes.CreateNewReview:
                workflowCode = WorkflowCodes.Review;
                isProjectBound = false;
                intakeResultCode = null;
                initialStageCode = ReviewStageCodes.ProjectSetup;
                return true;
            default:
                workflowCode = string.Empty;
                isProjectBound = false;
                intakeResultCode = null;
                initialStageCode = null;
                return false;
        }
    }

    private async Task<EmailSuggestedActionExecutionResult> StartWorkflowAsync(
        EmailSuggestedActionExecutionCommand command,
        string workflowCode,
        bool isProjectBound,
        string? intakeResultCode,
        string? initialStageCode,
        CancellationToken cancellationToken)
    {
        var definitions = await _workflowQuery.GetActiveDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(
            d => string.Equals(d.Code, workflowCode, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return new EmailSuggestedActionExecutionResult(
                false, false, $"תבנית תהליך '{workflowCode}' לא נמצאה או אינה פעילה.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Resolve the source inbox row. A price-quote request need not have attachments, so the email
        // may not have been pre-ingested by the ACC pipeline (InboxMessageId is null). In that case the
        // action itself materializes the inbox row on demand (mirrors legacy
        // EmailContextViewModel.EnsureEmailInboxMessageForActionAsync) so the workflow can start.
        int inboxMessageId;
        int projectId;
        if (command.InboxMessageId is int existingInboxId && existingInboxId > 0)
        {
            var message = await db.EmailInboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == existingInboxId, cancellationToken)
                .ConfigureAwait(false);

            if (message is null)
            {
                return new EmailSuggestedActionExecutionResult(false, false, "המייל לא נמצא.");
            }

            inboxMessageId = message.Id;
            projectId = message.ProjectId;
        }
        else
        {
            var (materializedId, materializedProjectId, materializeError) =
                await EnsureInboxMessageAsync(db, command.GmailSource, cancellationToken).ConfigureAwait(false);

            if (materializeError is not null)
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Email.StartWorkflow",
                    $"workflow={workflowCode} materialize FAILED: {materializeError}");
                return new EmailSuggestedActionExecutionResult(false, true, materializeError);
            }

            inboxMessageId = materializedId;
            projectId = materializedProjectId;

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.StartWorkflow",
                $"workflow={workflowCode} materialized inbox row #{inboxMessageId} (project={projectId})");
        }

        // Inbox rows always carry a ProjectId (office default when unassigned). It backs the
        // office placeholder for project-independent starts (Proposal) and the owning project
        // for bound starts (Opinion) — matching legacy ActionExecutor behaviour.
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
                false,
                false,
                $"כבר קיים תהליך '{definition.Name}' עבור מייל זה (#{existing}).",
                InboxMessageId: inboxMessageId,
                WorkflowInstanceId: existing);
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.StartWorkflow",
            $"inbox={inboxMessageId} workflow={workflowCode} def={definition.Id} project={projectId} bound={isProjectBound} initialStage={initialStageCode ?? "(default)"} → starting");

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
                        IsProjectBound: isProjectBound,
                        InitialStageCode: initialStageCode),
                    cancellationToken)
                .ConfigureAwait(false);

            if (intakeResultCode is not null
                && result.CreatedTasks.Count > 0
                && _taskCompletion is not null
                && command.ActingUserId > 0)
            {
                return await CompleteProposalIntakeAsync(
                        result,
                        intakeResultCode,
                        command.ActingUserId,
                        definition.Name,
                        inboxMessageId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var message2 = result.CreatedTasks.Count > 0
                ? initialStageCode == ProposalStageCodes.ProjectSetup
                    ? $"נפתח תהליך הצעת מחיר #{result.Instance.Id} — בדוק בלוח המשימות את 'פתיחת פרויקט הצעת מחיר'."
                    : $"תהליך '{definition.Name}' הופעל בהצלחה ונוצרה משימה."
                : $"תהליך '{definition.Name}' הופעל (מופע #{result.Instance.Id}), אך לא נוצרו משימות לשלב הראשון.";

            return new EmailSuggestedActionExecutionResult(
                true,
                false,
                message2,
                InboxMessageId: inboxMessageId,
                WorkflowInstanceId: result.Instance.Id);
        }
        catch (Exception ex)
        {
            return new EmailSuggestedActionExecutionResult(
                false, false, $"שגיאה בהפעלת תהליך '{definition.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Completes the intake <c>IdentifyQuoteRequest</c> task created by Start, using the verdict
    /// already chosen on the email action (CreatePriceQuote / RejectPriceQuote). Advances the
    /// Proposal workflow without a second classification UI.
    /// </summary>
    private async Task<EmailSuggestedActionExecutionResult> CompleteProposalIntakeAsync(
        WorkflowStartResultDto start,
        string intakeResultCode,
        int actingUserId,
        string definitionName,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        var intakeTask = start.CreatedTasks[0];

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.StartWorkflow",
            $"instance={start.Instance.Id} auto-complete intake task={intakeTask.Id} result={intakeResultCode}");

        var completion = await _taskCompletion!
            .CompleteAsync(
                new CompleteTaskCommand(
                    intakeTask.Id,
                    QuoteRequestClassifiedEvent,
                    intakeResultCode,
                    CompletedTaskLinkIds: null,
                    actingUserId),
                cancellationToken)
            .ConfigureAwait(false);

        if (!completion.Success || !completion.TaskClosed)
        {
            var detail = completion.ErrorMessage
                         ?? (completion.TaskClosed ? null : "המשימה לא נסגרה (יעד עבודה פתוח / מדיניות סגירה) — אין התקדמות שלב");
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.StartWorkflow",
                $"instance={start.Instance.Id} intake auto-complete FAILED: success={completion.Success} taskClosed={completion.TaskClosed} {detail}");
            return new EmailSuggestedActionExecutionResult(
                false,
                true,
                $"תהליך '{definitionName}' הופעל (#{start.Instance.Id}) אך סיווג הקליטה לא הושלם: {detail}. נסה שוב או פתח את משימת הזיהוי ידנית.",
                InboxMessageId: inboxMessageId,
                WorkflowInstanceId: start.Instance.Id);
        }

        if (string.Equals(intakeResultCode, NotQuoteRequest, StringComparison.Ordinal))
        {
            return new EmailSuggestedActionExecutionResult(
                true,
                false,
                $"סומן כלא בקשת הצעת מחיר — תהליך #{start.Instance.Id} נסגר.",
                InboxMessageId: inboxMessageId,
                WorkflowInstanceId: start.Instance.Id);
        }

        var nextStage = completion.StageAdvanceResult?.AdvancedInstance?.CurrentStage?.Code
                        ?? (completion.StageAdvanceResult?.TargetStageId is int sid ? $"שלב #{sid}" : null)
                        ?? "השלב הבא";
        var advanced = completion.WorkflowAdvanced
                       || completion.StageAdvanceResult?.Action == StageCompletionActionDto.AutoAdvanced;

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Email.StartWorkflow",
            $"instance={start.Instance.Id} intake done result={intakeResultCode} advanced={advanced} next={nextStage}");

        return new EmailSuggestedActionExecutionResult(
            true,
            false,
            advanced
                ? $"פתיחת הצעת מחיר אושרה. התהליך #{start.Instance.Id} התקדם ל־{nextStage}. בדוק בלוח המשימות את המשימה הבאה (פתיחת פרויקט הצעה)."
                : $"פתיחת הצעת מחיר אושרה (תהליך #{start.Instance.Id}). המשימה הבאה אמורה להופיע בלוח המשימות.",
            InboxMessageId: inboxMessageId,
            WorkflowInstanceId: start.Instance.Id);
    }

    /// <summary>
    /// Materializes an <see cref="EmailInboxMessage"/> row on demand for a workflow-starting action when
    /// the email is not yet in the inbox DB. Idempotent (re-uses an existing row matched by
    /// RFC 2822 identity). Enforces the strict inbox policy: an RFC 2822 <c>Message-ID</c> is required.
    /// Assigns the default office project. Mirrors legacy
    /// <c>EmailContextViewModel.EnsureEmailInboxMessageForActionAsync</c>.
    /// </summary>
    private async Task<(int InboxMessageId, int ProjectId, string? Error)> EnsureInboxMessageAsync(
        SiNetSQLDbContext db,
        EmailGmailSourceIdentity? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return (0, 0, "חסר מידע על המייל להפעלת התהליך.");
        }

        // Strict policy: InternetMessageId (RFC 2822 Message-ID) is required and UNIQUE. The Gmail
        // message id is a mailbox-local runtime identifier and is not accepted for new rows.
        if (string.IsNullOrWhiteSpace(source.InternetMessageId))
        {
            return (0, 0, "לא ניתן לתייק את המייל: חסר מזהה Message-ID (RFC 2822).");
        }

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(source.InternetMessageId, source.GmailMessageId);

        // Idempotency: re-use an existing row for the same message if one already exists.
        var existing = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.MessageUniqueId == messageUniqueId || m.InternetMessageId == source.InternetMessageId)
            .Select(m => new { m.Id, m.ProjectId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return (existing.Id, existing.ProjectId, null);
        }

        var defaultProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken).ConfigureAwait(false);
        if (defaultProjectId <= 0)
        {
            return (0, 0, "לא נמצא פרויקט משרד ברירת מחדל לתיוק המייל.");
        }

        var threadUniqueId = EmailMessageIdentity.GetThreadUniqueId(
            source.References, source.InReplyTo, source.InternetMessageId);
        var threadKey = EmailMessageIdentity.GetThreadKey(threadUniqueId);

        var nowUtc = DateTime.UtcNow;
        var inboxMessage = new EmailInboxMessage
        {
            MessageUniqueId = messageUniqueId,
            InternetMessageId = source.InternetMessageId.Trim(),
            InReplyTo = source.InReplyTo,
            References = source.References,
            ThreadUniqueId = threadUniqueId,
            ThreadKey = threadKey,
            GmailThreadId = source.GmailThreadId,
            ProjectId = defaultProjectId,
            Subject = source.Subject ?? string.Empty,
            FromAddress = source.FromAddress ?? string.Empty,
            ReceivedUtc = source.ReceivedUtc?.ToUniversalTime() ?? nowUtc,
            Status = EmailInboxStatus.Pending,
            CreatedByLogin = Environment.UserName,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        try
        {
            db.EmailInboxMessages.Add(inboxMessage);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Lost a race (UNIQUE on MessageUniqueId/InternetMessageId) — re-read the winner.
            var winner = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.MessageUniqueId == messageUniqueId || m.InternetMessageId == source.InternetMessageId)
                .Select(m => new { m.Id, m.ProjectId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (winner is null)
                throw;

            return (winner.Id, winner.ProjectId, null);
        }

        return (inboxMessage.Id, inboxMessage.ProjectId, null);
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
