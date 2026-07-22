namespace SiNet.Application.Email.Acc;

/// <summary>
/// Per-attachment failure carried on <see cref="EmailMoveToProjectCoordinatorResult"/>
/// so the UI can show an explicit reason (not just a failure count).
/// <para>
/// Kind values mirror the native executor vocabulary:
/// <c>Locked</c>, <c>AlreadyMovedToProject</c>, <c>MissingInAcc</c>,
/// <c>DownloadFailed</c>, <c>NoFilingTag</c>, <c>FilingFailed</c>, <c>ZipFilingFailed</c>.
/// </para>
/// </summary>
public sealed record EmailMoveToProjectAttachmentFailure(
    int InboxAttachmentId,
    string FileName,
    string Kind,
    string? Detail = null);
