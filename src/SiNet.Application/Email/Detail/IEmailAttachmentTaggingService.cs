namespace SiNet.Application.Email.Detail;

public interface IEmailAttachmentTaggingService
{
    Task<IReadOnlyList<EmailInboxAttachmentTagState>> LoadInboxAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailProjectAlternativeOption>> LoadAlternativesAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<EmailProjectAlternativeOption?> CreateAlternativeAsync(
        int projectId,
        string name,
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

/// <summary>Host prompt for naming a new project alternative (V2 dialog).</summary>
public interface IEmailAlternativeNamePromptHost
{
    bool IsAvailable { get; }

    Task<string?> PromptForNewAlternativeNameAsync(
        IReadOnlyList<string> existingNames,
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
    bool IsDefault,
    bool IsCreateNew = false)
{
    public const int CreateNewId = -1;

    public static EmailProjectAlternativeOption CreateNewSentinel { get; } =
        new(CreateNewId, "+ חדש...", IsDefault: false, IsCreateNew: true);
}

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
