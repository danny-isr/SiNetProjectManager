using System.Collections.ObjectModel;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

internal sealed class EmailListRowDisplayCoordinator
{
    private readonly EmailListViewModel _owner;
    private readonly Action _rebuildDisplayGroups;
    private readonly Action _applyGrouping;
    private readonly Func<ProjectSummaryDto?> _getCurrentProject;

    public EmailListRowDisplayCoordinator(
        EmailListViewModel owner,
        Action rebuildDisplayGroups,
        Action applyGrouping,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        _owner = owner;
        _rebuildDisplayGroups = rebuildDisplayGroups;
        _applyGrouping = applyGrouping;
        _getCurrentProject = getCurrentProject;
    }

    public EmailListRow? FindRowById(string rowId) => ResolveSelectionRow(rowId);

    public EmailListRow? ResolveSelectionRow(string rowId)
    {
        foreach (var flatRow in _owner.FlatDisplayEmails)
        {
            if (string.Equals(flatRow.Id, rowId, StringComparison.Ordinal))
            {
                return flatRow;
            }
        }

        foreach (var group in _owner.DisplayGroups)
        {
            var fromGroup = group.Emails.FirstOrDefault(row =>
                string.Equals(row.Id, rowId, StringComparison.Ordinal));
            if (fromGroup is not null)
            {
                return fromGroup;
            }
        }

        var projectGroup = _owner.GetProjectGroup();
        if (projectGroup is not null)
        {
            foreach (var email in projectGroup.Emails)
            {
                if (string.Equals(email.Id, rowId, StringComparison.Ordinal))
                {
                    return email;
                }
            }
        }

        foreach (var email in _owner.Emails)
        {
            if (string.Equals(email.Id, rowId, StringComparison.Ordinal))
            {
                return email;
            }
        }

        return null;
    }

    public void ReplaceRowInDisplay(EmailListRow updated)
    {
        RunOnUiThread(() => ReplaceRowInDisplayCore(updated));
    }

    public void ApplyLocalEmailMutation(EmailListRow updated)
    {
        RunOnUiThread(() => ApplyLocalEmailMutationCore(updated));
    }

    public void RemoveRowFromDisplay(EmailListRow row)
    {
        RunOnUiThread(() => RemoveRowFromDisplayCore(row));
    }

    public void RebindSelectedEmail(EmailListRow updated)
    {
        _owner.SyncSelectedEmailInstance(updated);
    }

    public bool TrySelectByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress)
    {
        var (match, _) = FindByInboxCorrelation(messageUniqueId, internetMessageId, subject, fromAddress);

        if (match is null)
        {
            return false;
        }

        // Do not clear PendingTaskSelection here: a late ReplaceRows after project refresh
        // must still re-apply the known InboxMessageId patch from the task target.
        if (_owner.PendingTaskSelection?.InboxMessageId is int knownInboxId && knownInboxId > 0)
        {
            match = _owner.PatchRowInboxMessageId(match.Id, knownInboxId) ?? match;
        }

        _owner.SelectedEmail = match;
        return true;
    }

    private (EmailListRow? Match, string MatchedBy) FindByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress)
    {
        EmailListRow? match = null;

        if (!string.IsNullOrWhiteSpace(messageUniqueId) || !string.IsNullOrWhiteSpace(internetMessageId))
        {
            match = _owner.Emails.FirstOrDefault(row =>
                EmailMessageIdMatcher.Matches(row.InternetMessageId, internetMessageId)
                || EmailMessageIdMatcher.Matches(row.InternetMessageId, messageUniqueId));
            if (match is not null)
            {
                return (match, "messageId");
            }
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            match = _owner.Emails.FirstOrDefault(row =>
                string.Equals(row.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(fromAddress)
                    || row.Sender.Contains(fromAddress, StringComparison.OrdinalIgnoreCase)));
            if (match is not null)
            {
                return (match, "subject");
            }
        }

        return (null, "(none)");
    }

    public IReadOnlyList<EmailListRow> ApplyClientRowFilters(IReadOnlyList<EmailListRow> rows)
    {
        IEnumerable<EmailListRow> filtered = _owner.SelectedProjectLinkFilter switch
        {
            EmailProjectLinkFilter.Linked => rows.Where(static row => row.IsLinked),
            EmailProjectLinkFilter.Unlinked => rows.Where(static row => !row.IsLinked),
            _ => rows,
        };

        // FollowQuoteApproval: prefer the SendQuote Gmail thread when an anchor is present.
        if (!string.IsNullOrWhiteSpace(_owner.FollowQuoteThreadFilter))
        {
            var threadId = _owner.FollowQuoteThreadFilter.Trim();
            filtered = filtered.Where(row =>
                string.Equals(row.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
        }

        // AttachmentsOnly is enforced server-side via has:attachment.

        return filtered.ToList();
    }

    public void ReplaceRows(
        IReadOnlyList<EmailListRow> rows,
        string? preserveSelectionId = null,
        bool skipDisplayRebuild = false)
    {
        _owner.Emails.Clear();
        foreach (var row in rows)
        {
            _owner.Emails.Add(row);
        }

        _applyGrouping();
        if (!skipDisplayRebuild)
        {
            _rebuildDisplayGroups();
        }

        // Task-driven target wins over the first-row auto-selection: reloads triggered by the
        // project-context change land in any order relative to the explicit task selection.
        if (_owner.PendingTaskSelection is { } pending)
        {
            var (taskMatch, _) = FindByInboxCorrelation(
                pending.MessageUniqueId, pending.InternetMessageId, pending.Subject, pending.FromAddress);

            if (taskMatch is not null)
            {
                if (pending.InboxMessageId is int knownInboxId && knownInboxId > 0)
                {
                    taskMatch = _owner.PatchRowInboxMessageId(taskMatch.Id, knownInboxId) ?? taskMatch;
                }

                // Keep pending until an explicit task-select clears it, so a late reload
                // after TrySelectByInboxCorrelation still re-applies the inbox id patch.
                _owner.SelectedEmail = taskMatch;
                _owner.NotifyUnreadDisplayProperties();
                return;
            }
        }

        var projectGroup = _owner.GetProjectGroup();
        var selectionPool = _owner.FlatDisplayEmails.Count > 0 ? _owner.FlatDisplayEmails : _owner.Emails;
        _owner.SelectedEmail = preserveSelectionId is null
            ? selectionPool.FirstOrDefault() ?? projectGroup?.Emails.FirstOrDefault()
            : selectionPool.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? projectGroup?.Emails.FirstOrDefault(row => string.Equals(row.Id, preserveSelectionId, StringComparison.Ordinal))
              ?? selectionPool.FirstOrDefault()
              ?? projectGroup?.Emails.FirstOrDefault();
        _owner.NotifyUnreadDisplayProperties();
    }

    public void RefreshRowBackgrounds()
    {
        if (_owner.Emails.Count == 0)
        {
            return;
        }

        var updated = false;
        for (var index = 0; index < _owner.Emails.Count; index++)
        {
            var row = _owner.Emails[index];
            var background = EmailListRowMapper.ResolveRowBackgroundColor(
                row.LabelChipNames,
                row.IsFiledToProject,
                row.LabelProjectId ?? row.ProjectId,
                _getCurrentProject,
                row.IsProjectMismatch,
                row.HasThreadHistory);
            var isFiledToSameProject = EmailListRowMapper.IsFiledToSameProjectForMapping(
                row.IsFiledToProject,
                row.ProjectId,
                row.FiledProjectLabelPath,
                _getCurrentProject);
            if (background == row.RowBackgroundColor && isFiledToSameProject == row.IsFiledToSameProject)
            {
                continue;
            }

            _owner.Emails[index] = row with
            {
                RowBackgroundColor = background,
                IsFiledToSameProject = isFiledToSameProject,
            };
            updated = true;
        }

        if (updated)
        {
            _rebuildDisplayGroups();
            _owner.RaiseCommandStates();
        }
    }

    public void SetRowActionState(
        EmailListRow row,
        bool busy,
        string? statusText,
        string? errorText = null)
    {
        var updated = row with
        {
            IsActionBusy = busy,
            ActionStatusText = busy ? statusText : null,
            ActionErrorText = busy ? errorText : null,
        };
        ReplaceRowInDisplay(updated);
    }

    private void ReplaceRowInDisplayCore(EmailListRow updated)
    {
        var id = updated.Id;
        var replaced = false;

        for (var index = 0; index < _owner.Emails.Count; index++)
        {
            if (string.Equals(_owner.Emails[index].Id, id, StringComparison.Ordinal))
            {
                _owner.Emails[index] = updated;
                replaced = true;
            }
        }

        for (var index = 0; index < _owner.FlatDisplayEmails.Count; index++)
        {
            if (string.Equals(_owner.FlatDisplayEmails[index].Id, id, StringComparison.Ordinal))
            {
                _owner.FlatDisplayEmails[index] = updated;
                replaced = true;
            }
        }

        foreach (var group in _owner.DisplayGroups)
        {
            for (var index = 0; index < group.Emails.Count; index++)
            {
                if (string.Equals(group.Emails[index].Id, id, StringComparison.Ordinal))
                {
                    group.Emails[index] = updated;
                    replaced = true;
                }
            }
        }

        var projectGroup = _owner.GetProjectGroup();
        if (projectGroup is not null)
        {
            for (var index = 0; index < projectGroup.Emails.Count; index++)
            {
                if (string.Equals(projectGroup.Emails[index].Id, id, StringComparison.Ordinal))
                {
                    projectGroup.Emails[index] = updated;
                    replaced = true;
                }
            }
        }

        if (string.Equals(_owner.SelectedEmail?.Id, id, StringComparison.Ordinal))
        {
            _owner.SyncSelectedEmailInstance(updated);
        }

        if (replaced)
        {
            _owner.RaiseCommandStates();
        }
    }

    private void ApplyLocalEmailMutationCore(EmailListRow updated)
    {
        var shouldRemove = _owner.SelectedProjectLinkFilter switch
        {
            EmailProjectLinkFilter.Linked => !updated.IsLinked,
            EmailProjectLinkFilter.Unlinked => updated.IsLinked,
            _ => false,
        };

        if (shouldRemove)
        {
            RemoveRowFromDisplayCore(updated);
            RebindSelectionAfterRemoval(updated.Id);
            _owner.RaiseCommandStates();
            return;
        }

        UpdateEmailInSourceCollection(updated);
        SyncProjectGroupMembership(updated);
        _rebuildDisplayGroups();
        RebindSelectionAfterMutation(updated.Id);
        _owner.RaiseCommandStates();
    }

    private void UpdateEmailInSourceCollection(EmailListRow updated)
    {
        for (var index = 0; index < _owner.Emails.Count; index++)
        {
            if (string.Equals(_owner.Emails[index].Id, updated.Id, StringComparison.Ordinal))
            {
                _owner.Emails[index] = updated;
                return;
            }
        }
    }

    private void SyncProjectGroupMembership(EmailListRow updated)
    {
        var projectGroup = _owner.GetProjectGroup();
        if (projectGroup is null)
        {
            return;
        }

        if (updated.IsFiledToSameProject)
        {
            projectGroup.TryAddEmail(updated);
            return;
        }

        projectGroup.RemoveEmailById(updated.Id);
    }

    private void RebindSelectionAfterMutation(string rowId)
    {
        if (!string.Equals(_owner.SelectedEmail?.Id, rowId, StringComparison.Ordinal))
        {
            return;
        }

        _owner.SelectedEmail = FindVisibleRowById(rowId);
    }

    private void RebindSelectionAfterRemoval(string rowId)
    {
        if (!string.Equals(_owner.SelectedEmail?.Id, rowId, StringComparison.Ordinal))
        {
            return;
        }

        var projectGroup = _owner.GetProjectGroup();
        _owner.SelectedEmail = _owner.FlatDisplayEmails.FirstOrDefault()
            ?? projectGroup?.Emails.FirstOrDefault()
            ?? _owner.Emails.FirstOrDefault();
    }

    private EmailListRow? FindVisibleRowById(string rowId) => ResolveSelectionRow(rowId);

    private void RemoveRowFromDisplayCore(EmailListRow row)
    {
        RemoveRowById(_owner.Emails, row.Id);
        foreach (var group in _owner.DisplayGroups)
        {
            RemoveRowById(group.Emails, row.Id);
        }

        _owner.GetProjectGroup()?.RemoveEmailById(row.Id);
        RemoveRowById(_owner.FlatDisplayEmails, row.Id);

        if (string.Equals(_owner.SelectedEmail?.Id, row.Id, StringComparison.Ordinal))
        {
            _owner.SelectedEmailId = null;
        }

        _rebuildDisplayGroups();
    }

    private static void RemoveRowById(ObservableCollection<EmailListRow> rows, string rowId)
    {
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (string.Equals(rows[index].Id, rowId, StringComparison.Ordinal))
            {
                rows.RemoveAt(index);
            }
        }
    }

    public void ApplyThreadFilingToPeers(string gmailThreadId, ProjectSummaryDto project)
    {
        if (string.IsNullOrWhiteSpace(gmailThreadId))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            var peerIds = new HashSet<string>(StringComparer.Ordinal);
            CollectPeerIds(_owner.Emails, gmailThreadId, peerIds);
            CollectPeerIds(_owner.FlatDisplayEmails, gmailThreadId, peerIds);
            foreach (var group in _owner.DisplayGroups)
            {
                CollectPeerIds(group.Emails, gmailThreadId, peerIds);
            }

            var projectGroup = _owner.GetProjectGroup();
            if (projectGroup is not null)
            {
                CollectPeerIds(projectGroup.Emails, gmailThreadId, peerIds);
            }

            foreach (var peerId in peerIds)
            {
                var peer = ResolveSelectionRow(peerId);
                if (peer is null)
                {
                    continue;
                }

                ApplyLocalEmailMutationCore(EmailListRowMapper.BuildOptimisticFiledRow(peer, project));
            }
        });
    }

    private static void CollectPeerIds(
        IEnumerable<EmailListRow> rows,
        string gmailThreadId,
        ISet<string> peerIds)
    {
        foreach (var row in rows)
        {
            if (string.Equals(row.ThreadId, gmailThreadId, StringComparison.OrdinalIgnoreCase))
            {
                peerIds.Add(row.Id);
            }
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
