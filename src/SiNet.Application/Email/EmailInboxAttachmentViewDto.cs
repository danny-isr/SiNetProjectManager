namespace SiNet.Application.Email;

/// <summary>
/// Attachment row for read-only preview hosts (e.g. OpenQuoteProject decision window).
/// </summary>
public sealed record EmailInboxAttachmentViewDto(
    int Id,
    string FileName,
    int AttachmentIndex,
    string? AccItemId)
{
    public bool CanOpenInAcc => !string.IsNullOrWhiteSpace(AccItemId);
}
