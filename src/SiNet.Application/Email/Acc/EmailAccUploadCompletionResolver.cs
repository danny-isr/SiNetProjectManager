namespace SiNet.Application.Email.Acc;

/// <summary>
/// Merges upload outcome, wait-for-completion, and DB sync into a single final ACC status for UI.
/// </summary>
public static class EmailAccUploadCompletionResolver
{
    public static EmailAccInboxStatus? ResolveFinalAccStatus(
        EmailAccUploadResult upload,
        EmailAccInboxStatus? waitStatus,
        EmailAccInboxStatus? syncedStatus)
    {
        if (waitStatus?.ProcessingStatus is EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.PartiallyUploaded
            or EmailAccProcessingStatus.MovedToProject)
        {
            return waitStatus;
        }

        if (syncedStatus is not null && !IsStuckProcessing(syncedStatus.ProcessingStatus))
        {
            return syncedStatus;
        }

        if (upload.Succeeded)
        {
            return BuildFromUploadSuccess(upload, waitStatus ?? syncedStatus);
        }

        if (upload.Outcome == EmailAccUploadOutcome.AlreadyProcessed)
        {
            return syncedStatus ?? waitStatus ?? BuildFromUploadSuccess(upload, null);
        }

        if (upload.Outcome is not EmailAccUploadOutcome.InProgress)
        {
            var failureDisplay = EmailAccUploadOutcomeDisplay.ResolveFailureMessage(upload);
            var baseStatus = syncedStatus ?? waitStatus;
            if (baseStatus is not null)
            {
                return baseStatus with
                {
                    ProcessingStatus = EmailAccProcessingStatus.Failed,
                    StatusDisplay = failureDisplay,
                };
            }
        }

        return syncedStatus ?? waitStatus;
    }

    private static EmailAccInboxStatus BuildFromUploadSuccess(
        EmailAccUploadResult upload,
        EmailAccInboxStatus? baseStatus)
    {
        var display = EmailAccUploadOutcomeDisplay.ResolveStatusMessage(upload);
        if (baseStatus is not null)
        {
            return baseStatus with
            {
                ProcessingStatus = EmailAccProcessingStatus.UploadedToAcc,
                StatusDisplay = display,
                ExistingInAccCount = Math.Max(baseStatus.ExistingInAccCount, upload.AttachmentsUploaded),
                TotalAttachments = Math.Max(baseStatus.TotalAttachments, upload.TotalAttachments),
            };
        }

        return new EmailAccInboxStatus(
            upload.MessageUniqueId ?? string.Empty,
            upload.InboxMessageId,
            EmailAccProcessingStatus.UploadedToAcc,
            null,
            display,
            null,
            upload.TotalAttachments,
            upload.AttachmentsUploaded,
            0,
            []);
    }

    private static bool IsStuckProcessing(EmailAccProcessingStatus status) =>
        status is EmailAccProcessingStatus.UploadInProgress
            or EmailAccProcessingStatus.ReconciliationRequired;
}
