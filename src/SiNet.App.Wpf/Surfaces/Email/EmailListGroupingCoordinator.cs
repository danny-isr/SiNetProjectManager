using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

internal sealed class EmailListGroupingCoordinator
{
    private readonly EmailListViewModel _owner;
    private readonly EmailListRowDisplayCoordinator _display;
    private readonly Func<EmailMailboxQuery> _buildQuery;
    private readonly Func<EmailMailboxQuery, int, EmailMailboxQuery> _buildProjectGroupQuery;

    public EmailListGroupingCoordinator(
        EmailListViewModel owner,
        EmailListRowDisplayCoordinator display,
        Func<EmailMailboxQuery> buildQuery,
        Func<EmailMailboxQuery, int, EmailMailboxQuery> buildProjectGroupQuery)
    {
        _owner = owner;
        _display = display;
        _buildQuery = buildQuery;
        _buildProjectGroupQuery = buildProjectGroupQuery;
    }

    public void ToggleGroupByLabel()
    {
        _owner.SetGroupByLabel(!_owner.GroupByLabel);
        RebuildDisplayGroups();
        ApplyGrouping();
    }

    public void ApplyGrouping()
    {
        var emailsView = _owner.EmailsView;
        if (emailsView is null)
        {
            return;
        }

        emailsView.GroupDescriptions.Clear();
        emailsView.Refresh();
    }

    public void ClearDisplayGroups()
    {
        _owner.SetProjectGroup(null);
        _owner.DisplayGroups.Clear();
        _owner.SetHasLabelGroups(false);
        NotifyDisplayGroupProperties();
    }

    public void ClearProjectGroup()
    {
        _owner.GetProjectGroup()?.ClearEmails();
        _owner.SetProjectGroup(null);
    }

    public void RebuildDisplayGroups()
    {
        var expandedByLabelId = _owner.DisplayGroups
            .Where(static g => !g.IsProjectGroup)
            .ToDictionary(static g => g.LabelId, static g => g.IsExpanded, StringComparer.Ordinal);

        var preservedProjectGroup = _owner.GetProjectGroup();
        _owner.DisplayGroups.Clear();

        var result = EmailListGroupBuilder.Rebuild(
            new EmailListGroupBuilder.RebuildInput(
                _owner.Emails,
                _owner.AvailableLabels,
                preservedProjectGroup,
                _owner.GetProjectContext()?.ProjectLabelName,
                _owner.GroupByLabel,
                expandedByLabelId),
            CreateLabelGroup);

        _owner.SetProjectGroup(preservedProjectGroup);
        _owner.SetHasLabelGroups(result.HasLabelGroups);

        foreach (var group in result.DisplayGroups)
        {
            _owner.DisplayGroups.Add(group);
        }

        _owner.FlatDisplayEmails.Clear();
        foreach (var row in result.FlatDisplayRows)
        {
            _owner.FlatDisplayEmails.Add(row);
        }

        NotifyDisplayGroupProperties();
    }

    public async Task EnsureProjectGroupAsync(bool resetPaging)
    {
        var projectContext = _owner.GetProjectContext();
        if (projectContext is null || !_owner.IsConnected)
        {
            ClearProjectGroup();
            return;
        }

        var header = projectContext.GroupHeaderDisplay;
        var projectGroup = _owner.GetProjectGroup();
        if (projectGroup is null
            || !string.Equals(projectGroup.LabelDisplayName, header, StringComparison.Ordinal))
        {
            _owner.SetProjectGroup(new EmailLabelGroupViewModel(
                ResolveProjectGroupLabelId(),
                header,
                LoadMoreEmailsForGroupAsync,
                LoadAllEmailsForGroupAsync,
                EmailListGroupKind.Project));
        }

        if (resetPaging)
        {
            _owner.GetProjectGroup()?.ClearEmails();
            _owner.GetProjectGroup()?.ResetPagingState();
            await LoadProjectGroupPageAsync(_owner.GetProjectGroup()!, isInitialPage: true).ConfigureAwait(true);
        }
    }

    public async Task LoadMoreEmailsForGroupAsync(EmailLabelGroupViewModel group)
    {
        if (!_owner.IsConnected || !group.SupportsRemotePaging)
        {
            return;
        }

        if (group.IsProjectGroup && _owner.GetProjectContext() is null)
        {
            return;
        }

        if (!group.IsProjectGroup && string.IsNullOrWhiteSpace(group.LabelId))
        {
            return;
        }

        group.IsExpanded = true;
        group.IsLoading = true;
        group.ErrorMessage = null;
        _owner.SetStatusMessage(group.IsProjectGroup
            ? $"טוען מיילים של הפרויקט {group.LabelDisplayName}..."
            : $"טוען מיילים מהלייבל {group.LabelDisplayName}...");

        try
        {
            if (group.IsProjectGroup)
            {
                await LoadProjectGroupPageAsync(group, isInitialPage: group.NextPageToken is null).ConfigureAwait(true);
            }
            else
            {
                var query = BuildGroupQuery(group);
                var page = await _owner.EmailGateway
                    .GetMailboxPageAsync(query, group.NextPageToken)
                    .ConfigureAwait(true);

                var rows = _display.ApplyClientRowFilters(await MapPageItemsAsync(page.Items).ConfigureAwait(true));
                foreach (var row in rows)
                {
                    group.TryAddEmail(row);
                }

                group.NextPageToken = page.NextPageToken;
                group.HasMore = page.HasNextPage;
                if (!page.HasNextPage)
                {
                    group.HasLoadedAll = true;
                }
            }

            if (group.IsProjectGroup)
            {
                RebuildDisplayGroups();
            }
        }
        catch (Exception ex)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים, אך הטעינה נעצרה בגלל שגיאה: {ex.Message}";
        }
        finally
        {
            group.IsLoading = false;
            group.NotifyHeaderChanged();
        }
    }

    public async Task LoadAllEmailsForGroupAsync(EmailLabelGroupViewModel group)
    {
        group.IsExpanded = true;
        group.ErrorMessage = null;

        for (var page = 0; page < EmailListViewModel.MaxPagesPerLabelLoad && group.HasMore; page++)
        {
            await LoadMoreEmailsForGroupAsync(group).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(group.ErrorMessage))
            {
                return;
            }
        }

        if (group.HasMore)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים. יש עוד — לחץ טען עוד.";
        }
        else
        {
            group.HasLoadedAll = true;
        }

        if (group.IsProjectGroup)
        {
            RebuildDisplayGroups();
        }

        group.NotifyHeaderChanged();
    }

    private EmailLabelGroupViewModel CreateLabelGroup(string labelId, string labelDisplayName) =>
        new(
            labelId,
            labelDisplayName,
            LoadMoreEmailsForGroupAsync,
            LoadAllEmailsForGroupAsync);

    private async Task LoadProjectGroupPageAsync(EmailLabelGroupViewModel group, bool isInitialPage)
    {
        if (_owner.GetProjectContext() is null)
        {
            return;
        }

        group.IsLoading = true;
        group.ErrorMessage = null;

        try
        {
            var query = _buildProjectGroupQuery(
                _buildQuery(),
                isInitialPage ? EmailListViewModel.ProjectEmailChunkSize : EmailListViewModel.PageSize);
            var page = await _owner.EmailGateway
                .GetMailboxPageAsync(query, group.NextPageToken)
                .ConfigureAwait(true);

            var rows = await MapPageItemsAsync(page.Items).ConfigureAwait(true);
            foreach (var row in rows)
            {
                group.TryAddEmail(row);
            }

            group.NextPageToken = page.NextPageToken;
            group.HasMore = page.HasNextPage;
            group.HasLoadedAll = !page.HasNextPage;
        }
        catch (Exception ex)
        {
            group.ErrorMessage = $"נטענו {group.LoadedCount} מיילים, אך הטעינה נעצרה בגלל שגיאה: {ex.Message}";
        }
        finally
        {
            group.IsLoading = false;
            group.NotifyHeaderChanged();
        }
    }

    private EmailMailboxQuery BuildGroupQuery(EmailLabelGroupViewModel group)
    {
        if (group.IsProjectGroup)
        {
            return _buildProjectGroupQuery(
                _buildQuery(),
                group.NextPageToken is null ? EmailListViewModel.ProjectEmailChunkSize : EmailListViewModel.PageSize);
        }

        return _buildQuery() with
        {
            MailboxScope = EmailMailboxScope.Label,
            LabelId = EmailListGroupBuilder.IsSyntheticLabelId(group.LabelId) ? null : group.LabelId,
            LabelName = group.LabelDisplayName,
        };
    }

    private string ResolveProjectGroupLabelId()
    {
        var projectContext = _owner.GetProjectContext();
        var projectLabelName = projectContext?.ProjectLabelName;
        if (string.IsNullOrWhiteSpace(projectLabelName))
        {
            return $"project:{projectContext?.ProjectId ?? 0}";
        }

        var match = _owner.AvailableLabels.FirstOrDefault(label =>
            string.Equals(label.Name, projectLabelName.Trim(), StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(match?.Id) ? match.Id : $"project:{projectLabelName.Trim()}";
    }

    private async Task<IReadOnlyList<EmailListRow>> MapPageItemsAsync(IReadOnlyList<EmailSummary> items)
    {
        var (rows, _) = await EmailListRowMapper.MapSummariesAsync(
            items,
            _owner.ThreadLinkQuery,
            () => _owner.GetCurrentProject()).ConfigureAwait(true);
        return rows;
    }

    private void NotifyDisplayGroupProperties()
    {
        _owner.NotifyDisplayGroupPropertiesChanged();
    }
}
