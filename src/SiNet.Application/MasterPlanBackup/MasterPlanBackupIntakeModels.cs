namespace SiNet.Application.MasterPlanBackup;

public enum EmailExternalDownloadPurpose
{
    ProjectAttachment = 0,
    MasterPlanBackup = 1
}

public enum MasterPlanBackupIntakeSource
{
    EmailNative = 1,
    JumboMail = 2,
    Manual = 3
}

public enum MasterPlanBackupIntakeStatus
{
    Queued = 1,
    Processing = 2,
    Restored = 3,
    SkippedNotNewer = 4,
    Failed = 5
}

public static class MasterPlanBackupIntakeStatusDisplay
{
    public static string ToHebrew(this MasterPlanBackupIntakeStatus status) => status switch
    {
        MasterPlanBackupIntakeStatus.Queued => "התקבל לתור",
        MasterPlanBackupIntakeStatus.Processing => "ממתין לעיבוד",
        MasterPlanBackupIntakeStatus.Restored => "שוחזר בהצלחה",
        MasterPlanBackupIntakeStatus.SkippedNotNewer => "דולג — הגיבוי אינו חדש יותר",
        MasterPlanBackupIntakeStatus.Failed => "נכשל",
        _ => status.ToString()
    };
}

public sealed record MasterPlanBackupIntakeRecord(
    int Id,
    MasterPlanBackupIntakeSource SourceType,
    string? GmailMessageId,
    string OriginalFileName,
    string IncomingPath,
    string? Sha256,
    DateTime ReceivedAtUtc,
    string ReceivedBy,
    DateTime? BackupFinishDate,
    MasterPlanBackupIntakeStatus Status,
    string? ResultMessage,
    DateTime? ProcessedAtUtc);

public sealed record MasterPlanBackupIntakeAcceptResult(
    MasterPlanBackupIntakeRecord Record,
    bool Duplicate);
