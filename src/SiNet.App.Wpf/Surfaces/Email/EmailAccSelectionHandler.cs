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
    private readonly IEmailAccIngestQueue? _ingestQueue;
    private readonly Action<EmailListRow>? _patchRow;
    private readonly Func<string, EmailListRow?>? _findRow;
    private readonly HashSet<string> _ingestionAttempted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busyRowIds = new(StringComparer.Ordinal);

    public EmailAccSelectionHandler(
        IEmailAccStatusService? statusService,
        IEmailAccUploadCoordinator? uploadCoordinator,
        Action<EmailListRow>? patchRow = null,
        IEmailAccIngestQueue? ingestQueue = null,
        Func<string, EmailListRow?>? findRow = null)
    {
        _statusService = statusService;
        _uploadCoordinator = uploadCoordinator;
        _ingestQueue = ingestQueue;
        _patchRow = patchRow;
        _findRow = findRow;
    }

    public event Action<string>? StatusMessageChanged;

    public bool IsRowBusy(string rowId) => _busyRowIds.Contains(rowId);

    public bool CanUpload(EmailListRow? row, bool isConnected) =>
        row is not null
        && isConnected
        && (_uploadCoordinator is not null || _ingestQueue is not null)
        && !_busyRowIds.Contains(row.Id)
        && EmailAccIngestGates.IsEligibleForAccIngest(row.HasAttachments, row.IsFiledToProject)
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

        if (_uploadCoordinator is null && _ingestQueue is null)
        {
            return "העלאה ל-ACC אינה זמינה.";
        }

        if (!EmailAccIngestGates.IsEligibleForAccIngest(row.HasAttachments, row.IsFiledToProject))
        {
            return "אין צרופות ולא משויך לפרויקט — לא מועלה ל-ACC.";
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
    /// On email selection, sync ACC status and passively ingest when N4.3-eligible
    /// (has attachments, or mailbox-filed to a project).
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

        // N4.3: unfiled + no attachments → status sync only, no AccService / PDF work.
        if (!EmailAccIngestGates.IsEligibleForAccIngest(row.HasAttachments, row.IsFiledToProject))
        {
            return (row, status);
        }

        if (EmailAccIngestGates.ShouldBlockPassiveUpload(status))
        {
            var lockedDisplay = status?.StatusDisplay ?? "המייל בטיפול על ידי משתמש אחר.";
            StatusMessageChanged?.Invoke(lockedDisplay);
            return (row, status);
        }

        if (IsTerminalAccStatus(status?.ProcessingStatus ?? row.AccProcessingStatus))
        {
            return (row, status);
        }

        if (_ingestionAttempted.Contains(row.Id))
        {
            if (EmailAccIngestGates.ShouldSkipRetryAfterAttempt(status))
            {
                return (row, status);
            }

            _ingestionAttempted.Remove(row.Id);
        }

        if (_uploadCoordinator is null && _ingestQueue is null)
        {
            var unavailable = row with
            {
                AccStatusDisplay = "העלאה ל-ACC אינה זמינה (Backend לא מוגדר)",
            };
            StatusMessageChanged?.Invoke(unavailable.AccStatusDisplay!);
            Patch(unavailable);
            return (unavailable, status);
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
            var upload = await UploadAsync(BuildUploadCommand(row), cancellationToken).ConfigureAwait(true);
            _ingestionAttempted.Add(row.Id);

            return await FinalizeAfterUploadAsync(row, upload, isStillSelected, cancellationToken)
                .ConfigureAwait(true);
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

            if (isStillSelected())
            {
                StatusMessageChanged?.Invoke(ex.Message);
            }

            Patch(failed);
            return (failed, status);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
            EnsureAccUploadBusyCleared(row.Id);
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
            var upload = await UploadAsync(BuildUploadCommand(row), cancellationToken).ConfigureAwait(true);
            var (patched, finalStatus) = await FinalizeAfterUploadAsync(
                row,
                upload,
                isStillSelected,
                cancellationToken).ConfigureAwait(true);

            if (!upload.Succeeded && upload.Outcome != EmailAccUploadOutcome.InProgress)
            {
                throw new InvalidOperationException(ResolveUserVisibleUploadMessage(upload));
            }

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
            return (failed, null);
        }
        finally
        {
            _busyRowIds.Remove(row.Id);
            EnsureAccUploadBusyCleared(row.Id);
        }
    }

    internal void ClearSessionStateForTests()
    {
        _ingestionAttempted.Clear();
        _busyRowIds.Clear();
    }

    private async Task<(EmailListRow Row, EmailAccInboxStatus? Status)> FinalizeAfterUploadAsync(
        EmailListRow row,
        EmailAccUploadResult upload,
        Func<bool> isStillSelected,
        CancellationToken cancellationToken)
    {
        EmailAccInboxStatus? waitStatus = null;

        if (upload.Outcome == EmailAccUploadOutcome.InProgress
            && !string.IsNullOrWhiteSpace(upload.MessageUniqueId)
            && _uploadCoordinator is not null)
        {
            if (isStillSelected())
            {
                StatusMessageChanged?.Invoke("המייל בטיפול על ידי משתמש אחר — ממתין לסיום…");
            }

            waitStatus = await _uploadCoordinator
                .WaitForCompletionAsync(
                    upload.MessageUniqueId,
                    ResolveActingUserLogin(),
                    TimeSpan.FromSeconds(5),
                    maxAttempts: 24,
                    shouldContinue: () => true,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        else if (isStillSelected())
        {
            StatusMessageChanged?.Invoke(ResolveUserVisibleUploadMessage(upload));
        }

        EmailAccInboxStatus? syncedStatus = null;
        if (_statusService is not null)
        {
            syncedStatus = await _statusService
                .SyncStatusWithRecoveryAsync(
                    row.InternetMessageId,
                    row.Id,
                    ResolveActingUserLogin(),
                    cancellationToken)
                .ConfigureAwait(true);
        }

        var finalStatus = EmailAccUploadCompletionResolver.ResolveFinalAccStatus(upload, waitStatus, syncedStatus);
        var patched = ApplyFinalAccStatus(row, finalStatus, upload);

        if (isStillSelected())
        {
            StatusMessageChanged?.Invoke(patched.AccStatusDisplay ?? ResolveUserVisibleUploadMessage(upload));
        }

        Patch(patched);
        return (patched, finalStatus);
    }

    private Task<EmailAccUploadResult> UploadAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken) =>
        _ingestQueue is not null
            ? _ingestQueue.EnqueueAsync(command, cancellationToken)
            : _uploadCoordinator!.UploadToAccInboxAsync(command, cancellationToken);

    private static string ResolveUserVisibleUploadMessage(EmailAccUploadResult upload) =>
        EmailAccIngestGates.MapAuthFailureMessage(upload.ErrorMessage)
        ?? EmailAccUploadOutcomeDisplay.ResolveStatusMessage(upload);

    private void Patch(EmailListRow row) => _patchRow?.Invoke(row);

    private void EnsureAccUploadBusyCleared(string rowId)
    {
        if (_findRow?.Invoke(rowId) is not { IsAccUploadBusy: true } current)
        {
            return;
        }

        Patch(current with { IsAccUploadBusy = false, AccUploadStatusText = null });
    }

    private static EmailAccUploadCommand BuildUploadCommand(EmailListRow row) =>
        new(
            row.Id,
            row.ThreadId ?? string.Empty,
            row.InternetMessageId,
            ResolveActingUserLogin(),
            AllowZeroAttachmentIngest: row.IsFiledToProject);

    private static bool IsTerminalAccStatus(EmailAccProcessingStatus status) =>
        status is EmailAccProcessingStatus.UploadedToAcc
            or EmailAccProcessingStatus.MovedToProject
            or EmailAccProcessingStatus.LockedByOtherUser;

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

    private static EmailListRow ApplyFinalAccStatus(
        EmailListRow row,
        EmailAccInboxStatus? finalStatus,
        EmailAccUploadResult upload)
    {
        var patched = ApplyAccStatus(row, finalStatus) with
        {
            IsAccUploadBusy = false,
            AccUploadStatusText = null,
        };

        // Native ingest returns InboxMessageId even when ACC status sync has not yet
        // populated finalStatus — bind it so the detail strip can load AccItemId for open.
        if (upload.InboxMessageId is int uploadedInboxId && uploadedInboxId > 0)
        {
            patched = patched with { InboxMessageId = uploadedInboxId };
        }

        if (upload.Succeeded
            && patched.AccProcessingStatus is EmailAccProcessingStatus.UploadInProgress
                or EmailAccProcessingStatus.ReconciliationRequired)
        {
            patched = patched with
            {
                AccProcessingStatus = EmailAccProcessingStatus.UploadedToAcc,
                AccStatusDisplay = EmailAccUploadOutcomeDisplay.ResolveStatusMessage(upload),
            };
        }

        return patched;
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
