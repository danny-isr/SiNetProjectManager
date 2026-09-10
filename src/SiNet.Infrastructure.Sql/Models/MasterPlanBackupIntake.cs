namespace SiNetSQL.Models;

/// <summary>Provenance for a MasterPlan bak copied into Incoming. Does not store bak bytes.</summary>
public sealed class MasterPlanBackupIntake
{
    public int Id { get; set; }
    public int SourceType { get; set; }
    public string? GmailMessageId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string IncomingPath { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime? BackupFinishDate { get; set; }
    public int Status { get; set; }
    public string? ResultMessage { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
