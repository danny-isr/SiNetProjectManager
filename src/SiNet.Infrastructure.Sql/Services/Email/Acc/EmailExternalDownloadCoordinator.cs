using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.MasterPlanBackup;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// External-download ACC orchestration. <see cref="IEmailExternalDownloadExecutor"/> is supplied by
/// DI (<c>NativeEmailExternalDownloadExecutor</c> in standalone; V2 may override with Legacy).
/// When absent, uploads return BackendNotAvailable.
/// </summary>
internal sealed class EmailExternalDownloadCoordinator(
    EmailAccInboxQueryService inboxQuery,
    IEmailExternalDownloadExecutor? downloadExecutor = null,
    IMasterPlanBackupIntakeService? backupIntake = null)
    : IEmailExternalDownloadCoordinator
{
    private readonly IEmailExternalDownloadExecutor? _downloadExecutor = downloadExecutor;
    private readonly IMasterPlanBackupIntakeService? _backupIntake = backupIntake;
    private readonly EmailAccInboxQueryService _inboxQuery =
        inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));

    public async Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        IProgress<EmailExternalDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Purpose == EmailExternalDownloadPurpose.MasterPlanBackup)
        {
            if (_backupIntake is null)
            {
                return new EmailExternalDownloadResult(
                    EmailExternalDownloadOutcome.Failed,
                    null,
                    null,
                    command.FileName,
                    "קליטת גיבוי MasterPlan אינה זמינה.");
            }

            var source = string.IsNullOrWhiteSpace(command.GmailMessageId)
                ? MasterPlanBackupIntakeSource.Manual
                : MasterPlanBackupIntakeSource.JumboMail;
            var accepted = await _backupIntake
                .AcceptAsync(
                    command.LocalFilePath,
                    source,
                    command.ActingUserLogin,
                    command.GmailMessageId,
                    cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new EmailExternalDownloadProgress(
                EmailExternalDownloadStage.Completed,
                accepted.Duplicate
                    ? "הגיבוי כבר נמצא בתור"
                    : "הגיבוי הועתק לתור Incoming",
                Percent: 100,
                FileName: command.FileName));
            return new EmailExternalDownloadResult(
                EmailExternalDownloadOutcome.Succeeded,
                AccItemId: null,
                AccFolderId: null,
                command.FileName,
                ErrorMessage: accepted.Duplicate ? "גיבוי כפול — לא הועתק שוב" : null);
        }

        if (_downloadExecutor is null)
        {
            return EmailExternalDownloadResult.BackendNotAvailable();
        }

        return await _downloadExecutor
            .UploadExternalFileAsync(command, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<EmailExternalDownloadItem>> ListExternalDownloadsAsync(
        string? internetMessageId,
        string gmailMessageId,
        CancellationToken cancellationToken = default) =>
        _inboxQuery.ListExternalDownloadsAsync(internetMessageId, gmailMessageId, cancellationToken);
}
