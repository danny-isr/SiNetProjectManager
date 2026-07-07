namespace SiNet.Application.Email.Acc;

public sealed record EmailAccUploadResult(
    EmailAccUploadOutcome Outcome,
    string? MessageUniqueId,
    int? InboxMessageId,
    int AttachmentsUploaded,
    int TotalAttachments,
    string? ErrorMessage,
    long DurationMs)
{
    public bool Succeeded =>
        Outcome is EmailAccUploadOutcome.Succeeded or EmailAccUploadOutcome.AlreadyProcessed;

    public static EmailAccUploadResult BackendNotAvailable(string? messageUniqueId = null) =>
        new(EmailAccUploadOutcome.BackendNotAvailable, messageUniqueId, null, 0, 0,
            "ACC upload backend is not configured.", 0);
}
