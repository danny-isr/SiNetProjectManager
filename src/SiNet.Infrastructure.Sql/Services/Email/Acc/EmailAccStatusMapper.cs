using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

public static class EmailAccStatusMapper
{
    public static EmailAccInboxStatus Map(
        string messageUniqueId,
        EmailInboxAccCacheRow? cache,
        AccInboxReconciliationResult? reconciliation,
        string? currentUserLogin)
    {
        if (cache is null)
        {
            return MapWithoutCache(messageUniqueId, reconciliation);
        }

        var lockStatus = BuildLockStatus(cache, currentUserLogin);
        var attachments = MapAttachments(reconciliation);
        var existing = attachments.Count(a => a.Presence == EmailAccAttachmentPresence.ExistsInAcc);
        var missing = attachments.Count(a => a.Presence == EmailAccAttachmentPresence.MissingInAcc);
        var total = attachments.Count > 0 ? attachments.Count : cache.AttachmentCount;
        var processingStatus = ResolveProcessingStatus(cache, lockStatus, reconciliation, existing, missing, total);
        var display = BuildDisplay(processingStatus, lockStatus, existing, missing, total);

        return new EmailAccInboxStatus(
            messageUniqueId,
            cache.Id,
            processingStatus,
            lockStatus,
            display,
            reconciliation?.InboxAccFolderId ?? cache.InboxAccFolderId,
            total,
            existing,
            missing,
            attachments);
    }

    private static EmailAccLockStatus BuildLockStatus(EmailInboxAccCacheRow cache, string? currentUserLogin)
    {
        if (cache.Status != EmailInboxStatus.Processing)
        {
            return new EmailAccLockStatus(false, false, cache.ProcessingByLogin, cache.ProcessingStartedAtUtc, false);
        }

        var stale = IsStaleLease(cache.ProcessingStartedAtUtc);
        var heldByCurrent = !string.IsNullOrWhiteSpace(currentUserLogin)
                            && string.Equals(cache.ProcessingByLogin, currentUserLogin, StringComparison.OrdinalIgnoreCase);

        return new EmailAccLockStatus(true, heldByCurrent, cache.ProcessingByLogin, cache.ProcessingStartedAtUtc, stale);
    }

    internal static bool IsStaleLease(DateTime? processingStartedAtUtc)
    {
        if (!processingStartedAtUtc.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - processingStartedAtUtc.Value
               > TimeSpan.FromMinutes(EmailAccLeasePolicy.LeaseTtlMinutes);
    }

    private static EmailAccProcessingStatus ResolveProcessingStatus(
        EmailInboxAccCacheRow cache,
        EmailAccLockStatus lockStatus,
        AccInboxReconciliationResult? reconciliation,
        int existing,
        int missing,
        int total)
    {
        if (cache.Status == EmailInboxStatus.Moved)
        {
            return EmailAccProcessingStatus.MovedToProject;
        }

        if (cache.Status == EmailInboxStatus.Error)
        {
            return missing > 0 && existing > 0
                ? EmailAccProcessingStatus.PartiallyUploaded
                : EmailAccProcessingStatus.Failed;
        }

        if (cache.Status == EmailInboxStatus.Processing)
        {
            if (existing > 0 && missing == 0 && (total == 0 || existing >= total))
            {
                return EmailAccProcessingStatus.UploadedToAcc;
            }

            if (missing > 0 && existing > 0)
            {
                return EmailAccProcessingStatus.PartiallyUploaded;
            }

            if (lockStatus.IsStaleLease)
            {
                return EmailAccProcessingStatus.ReconciliationRequired;
            }

            if (lockStatus.IsLocked && !lockStatus.IsHeldByCurrentUser)
            {
                return EmailAccProcessingStatus.LockedByOtherUser;
            }

            return EmailAccProcessingStatus.UploadInProgress;
        }

        if (reconciliation is null)
        {
            return cache.Status switch
            {
                EmailInboxStatus.Uploaded => EmailAccProcessingStatus.ReconciliationRequired,
                EmailInboxStatus.Pending => EmailAccProcessingStatus.PendingUpload,
                _ => EmailAccProcessingStatus.Unknown,
            };
        }

        if (missing > 0 && existing > 0)
        {
            return EmailAccProcessingStatus.PartiallyUploaded;
        }

        if (missing > 0 && existing == 0 && total > 0)
        {
            return EmailAccProcessingStatus.MissingInAcc;
        }

        if (existing > 0 || cache.Status == EmailInboxStatus.Uploaded)
        {
            return EmailAccProcessingStatus.UploadedToAcc;
        }

        return EmailAccProcessingStatus.PendingUpload;
    }

    private static string BuildDisplay(
        EmailAccProcessingStatus status,
        EmailAccLockStatus lockStatus,
        int existing,
        int missing,
        int total)
    {
        return status switch
        {
            EmailAccProcessingStatus.NotInDatabase => "לא הועלה ל-ACC",
            EmailAccProcessingStatus.PendingUpload => "ממתין להעלאה ל-ACC",
            EmailAccProcessingStatus.UploadInProgress => "העלאה ל-ACC מתבצעת…",
            EmailAccProcessingStatus.LockedByOtherUser =>
                $"בטיפול על ידי {lockStatus.ProcessingByLogin ?? "משתמש אחר"}",
            EmailAccProcessingStatus.PartiallyUploaded => $"חלקי ב-ACC ({existing}/{total})",
            EmailAccProcessingStatus.UploadedToAcc => "הועלה ל-ACC Inbox",
            EmailAccProcessingStatus.MovedToProject => "הועבר לפרויקט",
            EmailAccProcessingStatus.MissingInAcc => "חסר ב-ACC — נדרש רענון",
            EmailAccProcessingStatus.Failed => "העלאה ל-ACC נכשלה",
            EmailAccProcessingStatus.ReconciliationRequired => "נדרש רענון סטטוס ACC",
            _ => "סטטוס ACC לא ידוע",
        };
    }

    private static IReadOnlyList<EmailAttachmentAccStatus> MapAttachments(AccInboxReconciliationResult? reconciliation)
    {
        if (reconciliation?.Attachments is not { Count: > 0 } items)
        {
            return [];
        }

        return items
            .Select(item => new EmailAttachmentAccStatus(
                item.InboxAttachmentId,
                item.AttachmentIndex,
                item.FileName,
                MapPresence(item.Status),
                item.StatusText,
                item.LockedForEditing,
                item.MovedToProject,
                item.ProjectFileId,
                item.ProjectAlternativeId))
            .ToList();
    }

    private static EmailAccAttachmentPresence MapPresence(AccInboxAttachmentPresenceStatus status) => status switch
    {
        AccInboxAttachmentPresenceStatus.ExistsInAcc => EmailAccAttachmentPresence.ExistsInAcc,
        AccInboxAttachmentPresenceStatus.Locked => EmailAccAttachmentPresence.Locked,
        AccInboxAttachmentPresenceStatus.MissingInAcc => EmailAccAttachmentPresence.MissingInAcc,
        AccInboxAttachmentPresenceStatus.AlreadyMovedToProject => EmailAccAttachmentPresence.AlreadyMovedToProject,
        AccInboxAttachmentPresenceStatus.MetadataReadFailed => EmailAccAttachmentPresence.MetadataReadFailed,
        _ => EmailAccAttachmentPresence.Unknown,
    };

    public static string ResolveMessageUniqueId(string? internetMessageId, string gmailMessageId) =>
        EmailMessageIdentity.GetMessageUniqueId(internetMessageId, gmailMessageId);

    private static EmailAccInboxStatus MapWithoutCache(
        string messageUniqueId,
        AccInboxReconciliationResult? reconciliation)
    {
        var attachments = MapAttachments(reconciliation);
        var existing = attachments.Count(a => a.Presence == EmailAccAttachmentPresence.ExistsInAcc);
        var missing = attachments.Count(a => a.Presence == EmailAccAttachmentPresence.MissingInAcc);
        var total = attachments.Count;

        if (reconciliation is null || total == 0)
        {
            return new EmailAccInboxStatus(
                messageUniqueId,
                null,
                EmailAccProcessingStatus.NotInDatabase,
                null,
                BuildDisplay(EmailAccProcessingStatus.NotInDatabase, new EmailAccLockStatus(false, false, null, null, false), 0, 0, 0),
                null,
                0,
                0,
                0,
                attachments);
        }

        var processingStatus = missing > 0 && existing > 0
            ? EmailAccProcessingStatus.PartiallyUploaded
            : missing > 0
                ? EmailAccProcessingStatus.MissingInAcc
                : existing > 0
                    ? EmailAccProcessingStatus.UploadedToAcc
                    : EmailAccProcessingStatus.PendingUpload;

        var display = BuildDisplay(processingStatus, new EmailAccLockStatus(false, false, null, null, false), existing, missing, total);

        return new EmailAccInboxStatus(
            messageUniqueId,
            reconciliation.EmailMessageId > 0 ? reconciliation.EmailMessageId : null,
            processingStatus,
            null,
            display,
            reconciliation.InboxAccFolderId,
            total,
            existing,
            missing,
            attachments);
    }
}
