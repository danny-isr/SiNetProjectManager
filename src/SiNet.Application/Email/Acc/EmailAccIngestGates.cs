namespace SiNet.Application.Email.Acc;

/// <summary>Passive ingest gate helpers (legacy parity).</summary>
public static class EmailAccIngestGates
{
    public static bool IsIngestFullyComplete(EmailAccInboxStatus? status) =>
        status?.InboxMessageId is > 0
        && status.TotalAttachments > 0
        && status.ExistingInAccCount >= status.TotalAttachments;

    public static bool ShouldSkipRetryAfterAttempt(EmailAccInboxStatus? status) =>
        IsIngestFullyComplete(status);

    public static bool IsLockedByAnotherUser(EmailAccInboxStatus? status) =>
        status?.IsLockedByOtherUser == true
        || status?.ProcessingStatus == EmailAccProcessingStatus.LockedByOtherUser;

    public static bool ShouldBlockPassiveUpload(EmailAccInboxStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        if (IsLockedByAnotherUser(status))
        {
            return true;
        }

        return status.ProcessingStatus == EmailAccProcessingStatus.UploadInProgress
               && status.LockStatus?.IsHeldByCurrentUser == false;
    }

    public static string? MapAuthFailureMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        if (errorMessage.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("לא מחובר", StringComparison.Ordinal))
        {
            return "Gmail לא מחובר להעלאה — לחץ התחבר מחדש";
        }

        if (errorMessage.Contains("לא ניתן להעלות ל-ACC", StringComparison.Ordinal))
        {
            return errorMessage;
        }

        return null;
    }
}
