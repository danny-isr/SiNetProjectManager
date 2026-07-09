namespace SiNet.Application.Email.Detail;

public interface IEmailAttachmentTaggingService
{
    Task<IReadOnlyList<EmailInboxAttachmentTagState>> LoadInboxAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailProjectAlternativeOption>> LoadAlternativesAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailAttachmentTagTarget>> LoadTagTargetsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentTagValidationResult> ValidateTagAsync(
        EmailAttachmentTagValidationQuery query,
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentTagResult> SetTagAsync(
        EmailAttachmentTagCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmailInboxAttachmentTagState(
    int InboxAttachmentId,
    string FileName,
    int AttachmentIndex,
    int? ProjectFileId,
    string? ProjectFileTitle,
    int? ProjectAlternativeId,
    bool IsTaggable);

public sealed record EmailProjectAlternativeOption(
    int Id,
    string Name,
    bool IsDefault);

public sealed record EmailAttachmentTagTarget(
    int ProjectFileId,
    string DisplayName,
    bool HasAlternatives);

public sealed record EmailAttachmentTagValidationQuery(
    int InboxMessageId,
    int InboxAttachmentId,
    int ProjectFileId,
    int? ProjectAlternativeId);

public sealed record EmailAttachmentTagValidationResult(
    bool IsAllowed,
    string? BlockReason,
    bool WillCreateNewVersion);

public sealed record EmailAttachmentTagCommand(
    int InboxAttachmentId,
    int ProjectFileId,
    int? ProjectAlternativeId,
    int ActingUserId);

public sealed record EmailAttachmentTagResult(bool Succeeded, string? ErrorMessage);
