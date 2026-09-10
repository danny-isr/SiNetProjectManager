namespace SiNet.Application.MasterPlanBackup;

public interface IMasterPlanBackupIntakeService
{
    Task<MasterPlanBackupIntakeAcceptResult> AcceptAsync(
        string sourcePath,
        MasterPlanBackupIntakeSource sourceType,
        string receivedBy,
        string? gmailMessageId,
        CancellationToken cancellationToken = default);

    Task<MasterPlanBackupIntakeRecord?> GetByIncomingPathAsync(
        string incomingPath,
        CancellationToken cancellationToken = default);

    Task<MasterPlanBackupIntakeRecord?> GetBySha256Async(
        string sha256,
        CancellationToken cancellationToken = default);
}
