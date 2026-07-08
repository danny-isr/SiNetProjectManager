namespace SiNet.Application.Email.Acc;

/// <summary>Maps ACC upload outcomes to user-visible Hebrew status text.</summary>
public static class EmailAccUploadOutcomeDisplay
{
    public static string ResolveStatusMessage(EmailAccUploadResult upload) =>
        upload.Succeeded
            ? upload.TotalAttachments > 0
                ? $"הועלו {upload.AttachmentsUploaded}/{upload.TotalAttachments} צרופות ל-ACC"
                : "הועלה ל-ACC Inbox"
            : ResolveFailureMessage(upload);

    public static string? ResolveRowResultText(EmailAccUploadResult upload) =>
        upload.Succeeded
            ? upload.TotalAttachments > 0
                ? $"הועלו {upload.AttachmentsUploaded}/{upload.TotalAttachments} ל-ACC"
                : "הועלה ל-ACC"
            : upload.Outcome switch
            {
                EmailAccUploadOutcome.InProgress => "בטיפול על ידי משתמש אחר",
                EmailAccUploadOutcome.SkippedNoAttachments => "אין צרופות להעלאה",
                EmailAccUploadOutcome.SkippedNotRelevant => "מייל לא רלוונטי ל-ACC",
                EmailAccUploadOutcome.BackendNotAvailable => "העלאה ל-ACC אינה זמינה (Backend לא מוגדר)",
                EmailAccUploadOutcome.Failed => upload.ErrorMessage ?? "העלאה ל-ACC נכשלה",
                _ => upload.ErrorMessage,
            };

    public static string ResolveFailureMessage(EmailAccUploadResult upload) =>
        upload.Outcome switch
        {
            EmailAccUploadOutcome.SkippedNoAttachments => "אין צרופות Gmail להעלאה ל-ACC",
            EmailAccUploadOutcome.SkippedNotRelevant => "מייל לא רלוונטי ל-ACC — לא הועלה",
            EmailAccUploadOutcome.BackendNotAvailable =>
                "העלאה ל-ACC אינה זמינה — ודא שה-host מחובר ל-ACC Inbox",
            EmailAccUploadOutcome.InProgress => "המייל בטיפול על ידי משתמש אחר — ממתין לסיום…",
            EmailAccUploadOutcome.Failed => upload.ErrorMessage ?? "העלאה ל-ACC נכשלה",
            _ => upload.ErrorMessage ?? "העלאה ל-ACC נכשלה",
        };
}
