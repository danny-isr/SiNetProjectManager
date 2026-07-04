using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public sealed record AccInboxReconciliationRowViewModel(
    int? InboxAttachmentId,
    int AttachmentIndex,
    string FileName,
    string StatusText,
    AccInboxAttachmentPresenceStatus Status,
    bool ExistsInAcc,
    string? AccItemId,
    string? OpenAccProjectId,
    string? OpenAccFolderId,
    string? OpenAccItemId,
    bool MetadataReadFailed)
{
    public string AttachmentLabel =>
        InboxAttachmentId is int attachmentId
            ? $"#{attachmentId} / idx {AttachmentIndex}"
            : $"extra / idx {AttachmentIndex}";

    public bool HasLookupTarget =>
        !string.IsNullOrWhiteSpace(OpenAccProjectId)
        && (!string.IsNullOrWhiteSpace(OpenAccFolderId) || !string.IsNullOrWhiteSpace(FileName));

    public AccInboxAttachmentTruthStatus TruthStatus => Status.ToTruthStatus();
}
