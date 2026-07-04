namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// UI-agnostic metadata for one attachment on an email message. This slice exposes only
/// read-side details needed by the New System email window; opening/downloading remains a
/// later capability.
/// </summary>
public sealed record EmailMessageAttachmentDetails(
    string AttachmentId,
    string FileName,
    string ContentType,
    long? SizeBytes);
