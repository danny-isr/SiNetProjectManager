namespace SiNet.Application.Email.Acc;

public sealed record EmailExternalDownloadResult(
    EmailExternalDownloadOutcome Outcome,
    string? AccItemId,
    string? AccFolderId,
    string? FileName,
    string? ErrorMessage)
{
    public bool Succeeded => Outcome == EmailExternalDownloadOutcome.Succeeded;

    public static EmailExternalDownloadResult BackendNotAvailable() =>
        new(EmailExternalDownloadOutcome.BackendNotAvailable, null, null, null,
            "העלאת קובץ חיצוני ל-ACC אינה זמינה.");
}
