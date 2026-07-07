using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// ACC inbox selection orchestration for the Email Workbench: passive ingest on browse (legacy parity),
/// explicit upload, status sync, and recovery via Application ports.
/// </summary>
internal sealed class EmailAccSelectionHandler
{
    private readonly IEmailAccStatusService? _statusService;
    private readonly IEmailAccUploadCoordinator? _uploadCoordinator;
    private readonly Action<EmailListRow>? _patchRow;
    private readonly HashSet<string> _ingestionAttempted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busyRowIds = new(StringComparer.Ordinal);

    public EmailAccSelectionHandler(
        IEmailAccStatusService? statusService,
        IEmailAccUploadCoordinator? uploadCoordinator,
        Action<EmailListRow>? patchRow = null)
    {
        _statusService = statusService;
        _uploadCoordinator = uploadCoordinator;
        _patchRow = patchRow;
    }

    public event Action<string>? StatusMessageChanged;

    public bool IsRowBusy(string rowId) => _busyRowIds.Contains(rowId);

    public bool CanUpload(EmailListRow? row, bool isConnected) =>
        row is not null
        && isConnected
        && _uploadCoordinator is not null
        && row.HasAttachments
        && !_busyRowIds.Contains(row.Id)
        && row.AccProcessingStatus is not (
            EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.MovedToProject
            or EmailAccProcessingStatus.LockedByOtherUser
            or EmailAccProcessingStatus.UploadInProgress)
        && !row.IsAccUploadBusy;

    public string? DescribeUploadDisabledReason(EmailListRow? row, bool isConnected)
    {
        if (row is null)
        {
            return "לא נבחר מייל.";
        }

        if (!isConnected)
        {
            return "התחבר ל-Gmail.";
        }

        if (_uploadCoordinator is null)
        {
            return "העלאה ל-ACC אינה זמינה.";
        }

        if (!row.HasAttachments)
        {
            return "אין קבצים מצורפים.";
        }

        if (row.AccProcessingStatus == EmailAccProcessingStatus.LockedByOtherUser)
        {
            return row.AccStatusDisplay ?? "המייל בטיפול על ידי משתמש אחר.";
        }

        if (row.AccProcessingStatus is EmailAccProcessingStatus.UploadedToAcc or EmailAccProcessingStatus.MovedToProject)
        {
            return "כבר הועלה ל-ACC.";
        }

        if (row.IsAccUploadBusy || row.AccProcessingStatus == EmailAccProcessingStatus.UploadInProgress)
        {
            return "העלאה מתבצעת.";
        }

        return "הפעולה אינה זמינה.";
    }

    public async Task<(EmailListRow Row, EmailAccInboxStatus? Status)> LoadStatusAsync(
        EmailListRow row,
        CancellationToken cancellationToken = default)
    {
        if (_statusService is null)
        {
            return (row, null);
        }

        var loading = row with
        {
            IsAccStatusLoading = true,
            AccStatusDisplay = "בודק ACC…",
        };
        Patch(loading);

        try
        {
            var status = await _statusService
                .SyncStatusWithRecoveryAsync(
                    row.InternetMessageId,
                    row.Id,
                    ResolveActingUserLogin(),
                    cancellationToken)
                .ConfigureAwait(true);

            var patched = ApplyAccStatus(row, status);
            Patch(patched);
            return (patched, status);
        }
        catch (Exception ex)
        {
            var failed = row with
            {
                IsAccStatusLoading = false,
                AccProcessingStatus = EmailAccProcessingStatus.Unknown,
                AccStatusDisplay = $"שגיאת ACC: {ex.Message}",
            };
            Patch(failed);
            return (failed, null);
        }
    }

    /// <summary>
    /// Legacy parity: on email selection, sync ACC status and passively ingest when attachments exist.
    /// </summary>
    public async Task<(EmailListRow Row, EmailAccInboxStatus? Status)> TryPassiveIngestAsync(
        EmailListRow row,
        Func<bool> isStillSelected,
        CancellationToken cancellationToken = default)
    {
        if (_statusService is null)
        {
            return (row, null);
        }

        var (syncedRow, status) = await LoadStatusAsync(row, cancellationToken).ConfigureAwait(true);
        if (!isStillSelected())
        {
            return (syncedRow, status);
        }

        row = syncedRow;

        if (!row.HasAttachments)
        {
            return (row, status);
        }

        if (IsTerminalAccStatus(status?.ProcessingStatus ?? row.AccProcessingStatus))
        {
            return (row, status);
        }

        if (_ingestionAttempted.Contains(row.Id))
        {
            if (HasDbBackedAttachments(status))
            {
                return (row, status);
            }

            _ingestionAttempted.Remove(row.Id);
        }

        if (_uploadCoordinator is null)
        {
            return (row, status);
        }

        if (!_busyRowIds.Add(row.Id))
        {
            return (row, status);
        }

        var busy = row with
        {
            IsAccUploadBusy = true,
            AccUploadStatusText = "מעלה PDF וצרופות ל-ACC…",
            AccStatusDisplay = "מעלה PDF וצרופות ל-ACC…",
        };
        Patch(busy);
        row = busy;

        try
        {
            var command = BuildUploadCommand(row);
            var upload = await _uploadCoordinator
                .UploadToAccInboxAsync(command, cancellationToken)
                .ConfigureAwait(true);

            _ingestionAttempted.Add(row.Id);

            if (upload.Outcome == EmailAccUploadOutcome.InProgress
                && !string.IsNullOrWhiteSpace(upload.MessageUniqueId))
            {
                StatusMessageChanged?.Invoke("המייל בטיפול על ידי משתמש אחר — ממתין לסיום…");
                await _uploadCoordinator
                    .WaitForCompletionAsync(
                        upload.MessageUniqueId,
                        ResolveActingUserLogin(),
                        TimeSpan.FromSeconds(5),
                        maxAttempts: 24,
                        shouldContinue: isStillSelected,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            else if (upload.TotalAttachments > 0)
            {
                var progressText = upload.Succeeded
                    ? $"הועלו {upload.AttachmentsUploaded}/{upload.TotalAttachments} צרופות ל-ACC"
                    : upload.Outcome == EmailAccUploadOutcome.SkippedNoAttachments
                        ? "אין צרופות להעלאה"
                        : upload.Outcome == EmailAccUploadOutcome.SkippedNotRelevant
                            ? "מייל לא רלוונטי ל-ACC"
                            : null;

                if (!string.IsNullOrWhiteSpace(progressText))
                {
                    StatusMessageChanged?.Invoke(progressText);
                }
            }

            if (!isStillSelected())
            {
                return (busy, status);
            }

            var finalStatus = await _statusService
                .SyncStatusWithRecoveryAsync(
                    row.InternetMessageId,
                    row.Id,
                    ResolveActingUserLogin(),
                    cancellationToken)
                .ConfigureAwait(true);

            var patched = ApplyAccStatus(row, finalStatus) with
            {
                IsAccUploadBusy = false,
                AccUploadStatusText = ResolveUploadResultText(upload),
            };

            Patch(patched);
            return (patched, finalStatus);
        }
        catch (Exception ex)
        {
            var failed = row with
            {
                IsAccUploadBusy = false,
                AccUploadStatusText = null,
                AccStatusDisplay = ex.Message,
                AccProcessingStatus = EmailAccProcessingStatus.Failed,
            };
            StatusMessageChanged?.Invoke(ex.Message);
            Patch(failed);
            return (failed, status);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
        }
    }

    public async Task<(EmailListRow Row, EmailAccInboxStatus? Status)> UploadExplicitAsync(
        EmailListRow row,
        Func<bool> isStillSelected,
        CancellationToken cancellationToken = default)
    {
        if (!CanUpload(row, isConnected: true))
        {
            return (row, null);
        }

        if (!_busyRowIds.Add(row.Id))
        {
            return (row, null);
        }

        var busy = row with
        {
            IsAccUploadBusy = true,
            AccUploadStatusText = "מעלה ל-ACC Inbox…",
        };
        Patch(busy);

        try
        {
            var upload = await _uploadCoordinator!
                .UploadToAccInboxAsync(BuildUploadCommand(row), cancellationToken)
                .ConfigureAwait(true);

            if (upload.Outcome == EmailAccUploadOutcome.InProgress
                && !string.IsNullOrWhiteSpace(upload.MessageUniqueId))
            {
                StatusMessageChanged?.Invoke("המייל בטיפול על ידי משתמש אחר — ממתין לסיום…");
                await _uploadCoordinator
                    .WaitForCompletionAsync(
                        upload.MessageUniqueId,
                        ResolveActingUserLogin(),
                        TimeSpan.FromSeconds(5),
                        maxAttempts: 24,
                        shouldContinue: isStillSelected,
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            var status = await _statusService!
                .SyncStatusWithRecoveryAsync(
                    row.InternetMessageId,
                    row.Id,
                    ResolveActingUserLogin(),
                    cancellationToken)
                .ConfigureAwait(true);

            var patched = ApplyAccStatus(row, status) with
            {
                IsAccUploadBusy = false,
                AccUploadStatusText = ResolveUploadResultText(upload),
            };

            StatusMessageChanged?.Invoke(patched.AccStatusDisplay ?? "הועלה ל-ACC");

            if (!upload.Succeeded && upload.Outcome != EmailAccUploadOutcome.InProgress)
            {
                throw new InvalidOperationException(upload.ErrorMessage ?? "העלאה ל-ACC נכשלה.");
            }

            Patch(patched);
            return (patched, status);
        }
        catch (Exception ex)
        {
            var failed = row with
            {
                IsAccUploadBusy = false,
                AccUploadStatusText = null,
                AccStatusDisplay = ex.Message,
                AccProcessingStatus = EmailAccProcessingStatus.Failed,
            };
            StatusMessageChanged?.Invoke(ex.Message);
            Patch(failed);
            return (failed, null);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
        }
    }

    internal void ClearSessionStateForTests()
    {
        _ingestionAttempted.Clear();
        _busyRowIds.Clear();
    }

    private void Patch(EmailListRow row) => _patchRow?.Invoke(row);

    private static EmailAccUploadCommand BuildUploadCommand(EmailListRow row) =>
        new(
            row.Id,
            row.ThreadId ?? string.Empty,
            row.InternetMessageId,
            ResolveActingUserLogin());

    private static bool IsTerminalAccStatus(EmailAccProcessingStatus status) =>
        status is EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.MovedToProject
            or EmailAccProcessingStatus.LockedByOtherUser
            or EmailAccProcessingStatus.UploadInProgress;

    private static bool HasDbBackedAttachments(EmailAccInboxStatus? status) =>
        status?.InboxMessageId is > 0 && status.TotalAttachments > 0;

    private static string? ResolveUploadResultText(EmailAccUploadResult upload) =>
        upload.Succeeded
            ? upload.TotalAttachments > 0
                ? $"הועלו {upload.AttachmentsUploaded}/{upload.TotalAttachments} ל-ACC"
                : "הועלה ל-ACC"
            : upload.Outcome == EmailAccUploadOutcome.InProgress
                ? "בטיפול על ידי משתמש אחר"
                : upload.ErrorMessage;

    private static EmailListRow ApplyAccStatus(EmailListRow row, EmailAccInboxStatus? status)
    {
        if (status is null)
        {
            return row with { IsAccStatusLoading = false };
        }

        return row with
        {
            IsAccStatusLoading = false,
            AccProcessingStatus = status.ProcessingStatus,
            AccStatusDisplay = status.StatusDisplay,
            InboxMessageId = status.InboxMessageId ?? row.InboxMessageId,
        };
    }

    private static string ResolveActingUserLogin()
    {
        try
        {
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }
        catch
        {
            return Environment.UserName;
        }
    }
}
