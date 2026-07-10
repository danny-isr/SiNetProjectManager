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
    int? OverrideProjectId);

public sealed record EmailWorkflowContextDto(
    bool HasContext,
    string? ProjectDisplay,
    string? WorkflowFamilyDisplay,
    string? ConfidenceDisplay,
    int ActiveWorkflowCount,
    int AttachmentCount,
    bool IsAssociatedToProject = false);

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

public sealed record EmailSuggestedActionExecutionCommand(
    string ActionCode,
    int? InboxMessageId,
    int ActingUserId);

public sealed record EmailSuggestedActionExecutionResult(
    bool Succeeded,
    bool RequiresFollowUp,
    string? Message);

/// <summary>
/// Suggested-action codes for the email workflow pane (ported from legacy SuggestedActionType).
/// </summary>
public static class EmailSuggestedActionCodes
{
    public const string AssociateToExistingProject = nameof(AssociateToExistingProject);
    public const string CreatePriceQuote = nameof(CreatePriceQuote);
    public const string CreateNewReview = nameof(CreateNewReview);
    public const string RequestAuthorityInvitation = nameof(RequestAuthorityInvitation);
    public const string CreateOpinionProject = nameof(CreateOpinionProject);
    public const string CollectMaterial = nameof(CollectMaterial);
    public const string ForwardToDecision = nameof(ForwardToDecision);
    public const string FileOnly = nameof(FileOnly);
}
