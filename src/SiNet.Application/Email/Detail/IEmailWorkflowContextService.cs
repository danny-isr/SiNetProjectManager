namespace SiNet.Application.Email.Detail;

public interface IEmailWorkflowContextService
{
    Task<EmailWorkflowContextDto?> AnalyzeAsync(
        EmailWorkflowContextQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record EmailWorkflowContextQuery(
    int? InboxMessageId,
    string? GmailMessageId,
    int? OverrideProjectId,
    string? InternetMessageId = null);

public sealed record EmailWorkflowContextDto(
    bool HasContext,
    string? ProjectDisplay,
    string? WorkflowFamilyDisplay,
    string? ConfidenceDisplay,
    int ActiveWorkflowCount,
    int AttachmentCount,
    bool IsAssociatedToProject = false,
    /// <summary>True when an active Proposal instance is already bound to this inbox email.</summary>
    bool HasActiveProposalForEmail = false,
    /// <summary>Short Hebrew banner, e.g. "נפתח תהליך הצעת מחיר #5 — בחירת סוג פרויקט".</summary>
    string? ActiveProposalSummary = null);

public interface IEmailSuggestedActionService
{
    IReadOnlyList<EmailSuggestedActionDto> BuildActions(EmailWorkflowContextDto context);

    string? SelectedActionCode { get; }
}

public sealed record EmailSuggestedActionDto(
    string ActionCode,
    string DisplayName,
    string? Description,
    int SortOrder);

public interface IEmailSuggestedActionExecutionService
{
    Task<EmailSuggestedActionExecutionResult> ExecuteAsync(
        EmailSuggestedActionExecutionCommand command,
        CancellationToken cancellationToken = default);
}

/// <param name="ActionCode">The suggested-action code being executed.</param>
/// <param name="InboxMessageId">Persisted inbox row id, when the email is already in the inbox DB.</param>
/// <param name="ActingUserId">The acting user id.</param>
/// <param name="GmailSource">
/// Optional Gmail message identity, supplied when <paramref name="InboxMessageId"/> is null so that a
/// workflow-starting action (e.g. <c>CreatePriceQuote</c>) can materialize an inbox row on demand — a
/// price-quote request need not have attachments, so it is not pre-ingested by the ACC pipeline.
/// </param>
public sealed record EmailSuggestedActionExecutionCommand(
    string ActionCode,
    int? InboxMessageId,
    int ActingUserId,
    EmailGmailSourceIdentity? GmailSource = null);

/// <summary>
/// Minimal Gmail message identity needed to materialize an <c>EmailInboxMessage</c> on demand.
/// <see cref="InternetMessageId"/> (RFC 2822 Message-ID) is required by the strict inbox policy.
/// </summary>
public sealed record EmailGmailSourceIdentity(
    string GmailMessageId,
    string? InternetMessageId,
    string? References,
    string? InReplyTo,
    string? Subject,
    string? FromAddress,
    DateTime? ReceivedUtc,
    string? GmailThreadId);

public sealed record EmailSuggestedActionExecutionResult(
    bool Succeeded,
    bool RequiresFollowUp,
    string? Message,
    int? InboxMessageId = null,
    int? WorkflowInstanceId = null);

/// <summary>
/// Suggested-action codes for the email workflow pane (ported from legacy SuggestedActionType).
/// </summary>
public static class EmailSuggestedActionCodes
{
    public const string AssociateToExistingProject = nameof(AssociateToExistingProject);
    public const string CreatePriceQuote = nameof(CreatePriceQuote);
    /// <summary>
    /// Marks the email as not a price-quote request: starts Proposal and immediately completes intake
    /// with <c>NotQuoteRequest</c> (terminal reject). Offered beside <see cref="CreatePriceQuote"/> so the
    /// operator does not need a second classification dialog.
    /// </summary>
    public const string RejectPriceQuote = nameof(RejectPriceQuote);
    public const string CreateNewReview = nameof(CreateNewReview);
    public const string RequestAuthorityInvitation = nameof(RequestAuthorityInvitation);
    public const string CreateOpinionProject = nameof(CreateOpinionProject);
    public const string CollectMaterial = nameof(CollectMaterial);
    public const string ForwardToDecision = nameof(ForwardToDecision);
    public const string FileOnly = nameof(FileOnly);
}
