using System.Diagnostics;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

internal sealed class EmailListFilingCoordinator
{
    private readonly EmailListViewModel _owner;
    private readonly EmailListRowDisplayCoordinator _display;
    private readonly EmailListPagingCoordinator _paging;

    public EmailListFilingCoordinator(
        EmailListViewModel owner,
        EmailListRowDisplayCoordinator display,
        EmailListPagingCoordinator paging)
    {
        _owner = owner;
        _display = display;
        _paging = paging;
    }

    public bool CanFileEmailToProject(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _owner.FilingService is not null
        && row is not null
        && !IsFiledToSameProject(row)
        && _owner.GetCurrentProject() is not null
        && (_owner.GetCurrentUserId() ?? 0) > 0;

    public bool CanUnfileEmail(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _owner.FilingService is not null
        && row is { IsFiledToProject: true }
        && (_owner.GetCurrentUserId() ?? 0) > 0;

    public bool CanSetEmailStatus(EmailListRow? row) =>
        CanExecuteWriteAction(row)
        && _owner.StatusService is not null
        && row is not null
        && (_owner.GetCurrentUserId() ?? 0) > 0;

    public bool IsActionEnabled(EmailListRow? row, EmailContextMenuAction action) =>
        action switch
        {
            EmailContextMenuAction.FileToProject => CanFileEmailToProject(row),
            EmailContextMenuAction.Unfile => CanUnfileEmail(row),
            EmailContextMenuAction.MarkPending or EmailContextMenuAction.MarkPersonal or EmailContextMenuAction.MarkIrrelevant
                => CanSetEmailStatus(row),
            _ => false,
        };

    public string? GetContextMenuDisabledReason(EmailListRow? row, EmailContextMenuAction action)
    {
        if (IsActionEnabled(row, action))
        {
            return null;
        }

        return action switch
        {
            EmailContextMenuAction.FileToProject => DescribeFileToProjectDisabledReason(row),
            EmailContextMenuAction.Unfile => DescribeUnfileDisabledReason(row),
            EmailContextMenuAction.MarkPending or EmailContextMenuAction.MarkPersonal or EmailContextMenuAction.MarkIrrelevant
                => DescribeSetStatusDisabledReason(row),
            _ => "הפעולה אינה זמינה.",
        };
    }

    public async Task FileEmailToProjectAsync(EmailListRow? row)
    {
        if (row is null)
        {
            _owner.SetLoadWarning(DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.");
            return;
        }

        if (_owner.FilingService is null)
        {
            _owner.SetLoadWarning(DescribeFileToProjectDisabledReason(row));
            return;
        }

        if (_owner.GetCurrentProject() is not { } project)
        {
            _owner.SetLoadWarning(DescribeFileToProjectDisabledReason(row));
            return;
        }

        var actingUserId = _owner.GetCurrentUserId();
        if (actingUserId is null or <= 0)
        {
            _owner.SetLoadWarning(DescribeFileToProjectDisabledReason(row));
            return;
        }

        if (IsFiledToSameProject(row))
        {
            _owner.SetLoadWarning(DescribeFileToProjectDisabledReason(row));
            return;
        }

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: "משייך מייל לפרויקט...",
            rowStatusText: "משייך לפרויקט...",
            successStatusMessage: "המייל שויך לפרויקט בהצלחה.",
            serviceCall: async () =>
            {
                var result = await _owner.FilingService!.FileToProjectAsync(new FileEmailToProjectCommand(
                    project.ProjectId,
                    actingUserId.Value,
                    row.Id,
                    row.InboxMessageId,
                    row.ThreadId,
                    row.InternetMessageId)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "שיוך לפרויקט נכשל.");
            },
            onSuccessLocalUpdate: currentRow => RefreshRowAfterFileAsync(currentRow, project),
            failureMessagePrefix: "שיוך לפרויקט נכשל").ConfigureAwait(true);
    }

    public async Task UnfileEmailAsync(EmailListRow? row)
    {
        if (row is null)
        {
            _owner.SetLoadWarning(DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.");
            return;
        }

        if (_owner.FilingService is null)
        {
            _owner.SetLoadWarning(DescribeUnfileDisabledReason(row));
            return;
        }

        var actingUserId = _owner.GetCurrentUserId();
        if (actingUserId is null or <= 0)
        {
            _owner.SetLoadWarning(DescribeUnfileDisabledReason(row));
            return;
        }

        if (!row.IsFiledToProject)
        {
            _owner.SetLoadWarning(DescribeUnfileDisabledReason(row));
            return;
        }

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: "מבטל שיוך מייל...",
            rowStatusText: "מבטל שיוך...",
            successStatusMessage: "שיוך המייל לפרויקט בוטל.",
            serviceCall: async () =>
            {
                var result = await _owner.FilingService!.UnfileFromProjectAsync(new UnfileEmailCommand(
                    actingUserId.Value,
                    row.Id,
                    row.InboxMessageId,
                    row.ThreadId,
                    row.InternetMessageId,
                    row.FiledProjectLabelPath)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "ביטול שיוך נכשל.");
            },
            onSuccessLocalUpdate: RefreshRowAfterUnfileAsync,
            failureMessagePrefix: "ביטול שיוך נכשל").ConfigureAwait(true);
    }

    public async Task SetEmailStatusAsync(EmailListRow? row, EmailTriageStatus status)
    {
        if (row is null)
        {
            _owner.SetLoadWarning(DescribeWriteActionBlockedReason(null) ?? "לא נבחר מייל.");
            return;
        }

        if (_owner.StatusService is null)
        {
            _owner.SetLoadWarning(DescribeSetStatusDisabledReason(row));
            return;
        }

        var actingUserId = _owner.GetCurrentUserId();
        if (actingUserId is null or <= 0)
        {
            _owner.SetLoadWarning(DescribeSetStatusDisabledReason(row));
            return;
        }

        var (startingMessage, rowStatusText, successMessage) = status switch
        {
            EmailTriageStatus.Pending => ("מסמן מייל כממתין לטיפול...", "מסמן כממתין...", "המייל סומן כממתין לטיפול."),
            EmailTriageStatus.Personal => ("מסמן מייל כאישי...", "מסמן כאישי...", "המייל סומן כאישי."),
            EmailTriageStatus.Irrelevant => ("מסמן מייל כלא רלוונטי...", "מסמן כלא רלוונטי...", "המייל סומן כלא רלוונטי."),
            _ => ("מעדכן סטטוס מייל...", "מעדכן סטטוס...", "סטטוס המייל עודכן."),
        };

        var removeOnSuccess = status is EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant;

        await ExecuteRowActionAsync(
            row,
            startingStatusMessage: startingMessage,
            rowStatusText: rowStatusText,
            successStatusMessage: successMessage,
            serviceCall: async () =>
            {
                var result = await _owner.StatusService!.SetStatusAsync(new SetEmailStatusCommand(
                    row.Id,
                    row.ThreadId,
                    status,
                    actingUserId.Value,
                    row.InboxMessageId,
                    row.ThreadUniqueId)).ConfigureAwait(true);
                return (result.Succeeded, result.ErrorMessage ?? "עדכון סטטוס נכשל.");
            },
            onSuccessLocalUpdate: currentRow => RefreshRowAfterStatusAsync(currentRow, status),
            failureMessagePrefix: "עדכון סטטוס נכשל",
            removeRowOnSuccess: removeOnSuccess).ConfigureAwait(true);
    }

    private async Task ExecuteRowActionAsync(
        EmailListRow row,
        string startingStatusMessage,
        string rowStatusText,
        string successStatusMessage,
        Func<Task<(bool Succeeded, string? ErrorMessage)>> serviceCall,
        Func<EmailListRow, Task<EmailListRow?>> onSuccessLocalUpdate,
        string failureMessagePrefix,
        bool removeRowOnSuccess = false)
    {
        if (_owner.IsRowActionBusy(row.Id))
        {
            return;
        }

        _owner.AddBusyRowId(row.Id);
        var totalSw = Stopwatch.StartNew();
        long serviceMs = 0;
        long localUpdateMs = 0;
        long refreshMs = 0;

        try
        {
            _owner.SetStatusMessage(startingStatusMessage);
            _display.SetRowActionState(_display.FindRowById(row.Id) ?? row, busy: true, statusText: rowStatusText);
            _owner.RaiseCommandStates();

            var serviceSw = Stopwatch.StartNew();
            var (succeeded, errorMessage) = await serviceCall().ConfigureAwait(true);
            serviceMs = serviceSw.ElapsedMilliseconds;

            if (!succeeded)
            {
                _owner.SetLoadWarning(errorMessage ?? $"{failureMessagePrefix}.");
                var current = _display.FindRowById(row.Id) ?? row;
                _display.SetRowActionState(current, busy: false, statusText: null, errorText: _owner.LoadWarning);
                return;
            }

            _owner.SetLoadWarning(null);

            var localSw = Stopwatch.StartNew();
            var updatedRow = await onSuccessLocalUpdate(row).ConfigureAwait(true);
            localUpdateMs = localSw.ElapsedMilliseconds;

            if (updatedRow is null)
            {
                _owner.SetStatusMessage("מרענן רשימה...");
                var refreshSw = Stopwatch.StartNew();
                await _paging.ReloadForContextAsync().ConfigureAwait(true);
                refreshMs = refreshSw.ElapsedMilliseconds;
                _owner.SetStatusMessage(successStatusMessage);
                return;
            }

            var clearedRow = updatedRow with
            {
                IsActionBusy = false,
                ActionStatusText = null,
                ActionErrorText = null,
            };

            if (removeRowOnSuccess)
            {
                _display.RemoveRowFromDisplay(clearedRow);
            }
            else
            {
                _display.ApplyLocalEmailMutation(clearedRow);
            }

            _owner.SetStatusMessage(successStatusMessage);
        }
        catch (Exception ex)
        {
            _owner.SetLoadWarning($"{failureMessagePrefix}: {ex.Message}");
            var current = _display.FindRowById(row.Id) ?? row;
            _display.SetRowActionState(current, busy: false, statusText: null, errorText: _owner.LoadWarning);
        }
        finally
        {
            _owner.RemoveBusyRowId(row.Id);
            _owner.SetLastActionDiagnostics(
                $"total={totalSw.ElapsedMilliseconds}ms service={serviceMs}ms localUpdate={localUpdateMs}ms refresh={refreshMs}ms");
            Debug.WriteLine($"[PERF] EmailAction row={row.Id} {_owner.LastActionDiagnostics}");
            _owner.RaiseCommandStates();
        }
    }

    private async Task<EmailListRow?> RefreshRowAfterFileAsync(EmailListRow row, ProjectSummaryDto project)
    {
        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        if (refreshed is null || !refreshed.IsFiledToProject)
        {
            return EmailListRowMapper.BuildOptimisticFiledRow(row, project);
        }

        return refreshed with
        {
            ProjectId = project.ProjectId,
            ProjectNumber = project.ProjectNumber,
            ProjectName = project.ProjectName,
            ProjectDisplay = $"{project.ProjectNumber} — {project.ProjectName}",
            AssignedProjectName = $"{project.ProjectNumber} — {project.ProjectName}",
            IsFiledToProject = true,
            IsFiledToSameProject = true,
            IsAssigned = true,
            ProjectLinkState = EmailProjectLinkState.Linked,
            FiledProjectLabelPath = refreshed.FiledProjectLabelPath
                ?? $"{EmailGmailLabelNames.RootLabel}/{project.PlaceName ?? string.Empty}/{project.ProjectNumber} — {project.ProjectName}",
        };
    }

    private async Task<EmailListRow?> RefreshRowAfterUnfileAsync(EmailListRow row)
    {
        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        return refreshed ?? EmailListRowMapper.BuildOptimisticUnfiledRow(row);
    }

    private async Task<EmailListRow?> RefreshRowAfterStatusAsync(EmailListRow row, EmailTriageStatus status)
    {
        if (status is EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant)
        {
            return row;
        }

        var refreshed = await RefreshRowFromGmailAsync(row).ConfigureAwait(true);
        return refreshed ?? EmailListRowMapper.BuildOptimisticStatusRow(row, status);
    }

    private async Task<EmailListRow?> RefreshRowFromGmailAsync(EmailListRow row)
    {
        var summary = await _owner.EmailGateway.GetByIdAsync(row.Id).ConfigureAwait(true);
        if (summary is null)
        {
            return null;
        }

        var (rows, _) = await EmailListRowMapper.MapSummariesAsync(
            [summary],
            _owner.ThreadLinkQuery,
            () => _owner.GetCurrentProject()).ConfigureAwait(true);
        return rows.Count > 0 ? rows[0] : null;
    }

    private bool CanExecuteWriteAction(EmailListRow? row) =>
        row is not null && _owner.IsConnected && !_owner.IsRowActionBusy(row.Id);

    private bool IsFiledToSameProject(EmailListRow row) => row.IsFiledToSameProject;

    private string? DescribeWriteActionBlockedReason(EmailListRow? row)
    {
        if (row is null)
        {
            return "לא נבחר מייל.";
        }

        if (!_owner.IsConnected)
        {
            return "חבר Gmail לפני ביצוע פעולה.";
        }

        if (row is not null && _owner.IsRowActionBusy(row.Id))
        {
            return "פעולה כבר רצה על מייל זה.";
        }

        if (_owner.IsBusy)
        {
            return "המערכת עסוקה — נסה שוב בעוד רגע.";
        }

        return null;
    }

    private string? DescribeFileToProjectDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_owner.FilingService is null)
        {
            return "שירות שיוך מיילים לא זמין.";
        }

        if ((_owner.GetCurrentUserId() ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        if (_owner.GetCurrentProject() is null)
        {
            return "בחר פרויקט לפני שיוך מייל.";
        }

        if (row is not null && IsFiledToSameProject(row))
        {
            return "המייל כבר משויך לפרויקט הנבחר.";
        }

        return "לא ניתן לשייך מייל לפרויקט.";
    }

    private string? DescribeUnfileDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_owner.FilingService is null)
        {
            return "שירות שיוך מיילים לא זמין.";
        }

        if ((_owner.GetCurrentUserId() ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        if (row is not { IsFiledToProject: true })
        {
            return "המייל לא משויך לפרויקט.";
        }

        return "לא ניתן לבטל שיוך.";
    }

    private string? DescribeSetStatusDisabledReason(EmailListRow? row)
    {
        var blocked = DescribeWriteActionBlockedReason(row);
        if (blocked is not null)
        {
            return blocked;
        }

        if (_owner.StatusService is null)
        {
            return "שירות עדכון סטטוס מייל לא זמין.";
        }

        if ((_owner.GetCurrentUserId() ?? 0) <= 0)
        {
            return "לא ניתן לבצע פעולה — אין משתמש מחובר במערכת.";
        }

        return "לא ניתן לעדכן סטטוס.";
    }
}
