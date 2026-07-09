namespace SiNet.Application.Email.Detail;

public interface IEmailAttachmentTaggingService
{
    Task<IReadOnlyList<EmailAttachmentTagTarget>> LoadTagTargetsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentTagResult> SetTagAsync(
        EmailAttachmentTagCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmailAttachmentTagTarget(
    int ProjectFileId,
    string DisplayName,
    bool HasAlternatives);

public sealed record EmailAttachmentTagCommand(
    int InboxAttachmentId,
    int ProjectFileId,
    int? ProjectAlternativeId,
    int ActingUserId);

public sealed record EmailAttachmentTagResult(bool Succeeded, string? ErrorMessage);
