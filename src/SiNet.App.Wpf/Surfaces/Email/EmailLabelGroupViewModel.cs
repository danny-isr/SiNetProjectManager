using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Collapsible email group (project or label) with optional per-label Gmail paging.</summary>
public sealed class EmailLabelGroupViewModel : ObservableObject
{
    private readonly Func<EmailLabelGroupViewModel, Task> _loadMore;
    private readonly Func<EmailLabelGroupViewModel, Task> _loadAll;
    private readonly HashSet<string> _seenMessageIds = new(StringComparer.Ordinal);

    private bool _isExpanded = true;
    private bool _isLoading;
    private bool _hasLoadedAll;
    private string? _nextPageToken;
    private bool _hasMore = true;
    private string? _errorMessage;

    public EmailLabelGroupViewModel(
        string labelId,
        string labelDisplayName,
        Func<EmailLabelGroupViewModel, Task> loadMore,
        Func<EmailLabelGroupViewModel, Task> loadAll,
        EmailListGroupKind groupKind = EmailListGroupKind.Label)
    {
        LabelId = labelId ?? throw new ArgumentNullException(nameof(labelId));
        LabelDisplayName = labelDisplayName ?? throw new ArgumentNullException(nameof(labelDisplayName));
        _loadMore = loadMore ?? throw new ArgumentNullException(nameof(loadMore));
        _loadAll = loadAll ?? throw new ArgumentNullException(nameof(loadAll));
        GroupKind = groupKind;
        SupportsRemotePaging = groupKind == EmailListGroupKind.Project
            || !EmailListGroupBuilder.IsSyntheticLabelId(labelId);

        Emails = [];

        ToggleExpandCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ExpandCommand = new RelayCommand(_ => IsExpanded = true);
        CollapseCommand = new RelayCommand(_ => IsExpanded = false);
        LoadMoreForLabelCommand = new AsyncRelayCommand(() => _loadMore(this), () => !IsLoading && HasMore && SupportsRemotePaging);
        LoadAllForLabelCommand = new AsyncRelayCommand(() => _loadAll(this), () => !IsLoading && SupportsRemotePaging);
    }

    public EmailListGroupKind GroupKind { get; }

    public bool IsProjectGroup => GroupKind == EmailListGroupKind.Project;

    public bool IsPinned => IsProjectGroup;

    public bool SupportsRemotePaging { get; }

    public string LabelId { get; }

    public string LabelDisplayName { get; }

    public ObservableCollection<EmailListRow> Emails { get; }

    public IReadOnlyCollection<string> SeenMessageIds => _seenMessageIds;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => UiThread.Run(() => SetField(ref _isExpanded, value));
    }

    public bool IsLoading
    {
        get => _isLoading;
        internal set
        {
            UiThread.Run(() =>
            {
                if (SetField(ref _isLoading, value))
                {
                    NotifyHeaderChangedCore();
                    (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (LoadAllForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            });
        }
    }

    public bool HasLoadedAll
    {
        get => _hasLoadedAll;
        internal set
        {
            UiThread.Run(() =>
            {
                if (SetField(ref _hasLoadedAll, value))
                {
                    NotifyHeaderChangedCore();
                    (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            });
        }
    }

    public int LoadedCount => Emails.Count;

    public bool HasMore
    {
        get => _hasMore;
        internal set
        {
            UiThread.Run(() =>
            {
                if (SetField(ref _hasMore, value))
                {
                    NotifyHeaderChangedCore();
                    (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            });
        }
    }

    public string? NextPageToken
    {
        get => _nextPageToken;
        internal set => UiThread.Run(() => _nextPageToken = value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set
        {
            UiThread.Run(() =>
            {
                if (SetField(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(ShowGroupError));
                }
            });
        }
    }

    public bool ShowGroupError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string HeaderStatus
    {
        get
        {
            if (IsLoading)
            {
                return IsProjectGroup
                    ? $"📂 {LabelDisplayName} — טוען..."
                    : $"{LabelDisplayName} — טוען...";
            }

            var prefix = IsProjectGroup ? "📂 " : string.Empty;
            var status = $"{prefix}{LabelDisplayName} — {LoadedCount} נטענו";
            if (HasLoadedAll)
            {
                return $"{status} — הכול נטען";
            }

            if (HasMore && SupportsRemotePaging)
            {
                return $"{status} — יש עוד";
            }

            return status;
        }
    }

    public ICommand ToggleExpandCommand { get; }

    public ICommand ExpandCommand { get; }

    public ICommand CollapseCommand { get; }

    public ICommand LoadMoreForLabelCommand { get; }

    public ICommand LoadAllForLabelCommand { get; }

    internal bool TryAddEmail(EmailListRow row)
    {
        var added = false;
        UiThread.Run(() =>
        {
            if (!_seenMessageIds.Add(row.Id))
            {
                return;
            }

            Emails.Add(row);
            NotifyHeaderChangedCore();
            added = true;
        });
        return added;
    }

    internal bool RemoveEmailById(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        var removed = false;
        UiThread.Run(() =>
        {
            for (var index = Emails.Count - 1; index >= 0; index--)
            {
                if (string.Equals(Emails[index].Id, messageId, StringComparison.Ordinal))
                {
                    Emails.RemoveAt(index);
                    removed = true;
                }
            }

            if (_seenMessageIds.Remove(messageId))
            {
                removed = true;
            }

            if (removed)
            {
                NotifyHeaderChangedCore();
            }
        });
        return removed;
    }

    internal void ResetPagingState()
    {
        UiThread.Run(() =>
        {
            if (IsProjectGroup)
            {
                return;
            }

            _nextPageToken = null;
            _hasMore = true;
            _hasLoadedAll = false;
            _errorMessage = null;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ShowGroupError));
            NotifyHeaderChangedCore();
        });
    }

    internal void ClearEmails()
    {
        UiThread.Run(() =>
        {
            Emails.Clear();
            _seenMessageIds.Clear();
            if (!IsProjectGroup)
            {
                _nextPageToken = null;
                _hasMore = true;
                _hasLoadedAll = false;
                _errorMessage = null;
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(ShowGroupError));
                NotifyHeaderChangedCore();
                return;
            }

            _nextPageToken = null;
            _hasMore = true;
            _hasLoadedAll = false;
            _errorMessage = null;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(ShowGroupError));
            NotifyHeaderChangedCore();
        });
    }

    internal void NotifyHeaderChanged() => UiThread.Run(NotifyHeaderChangedCore);

    private void NotifyHeaderChangedCore()
    {
        OnPropertyChanged(nameof(LoadedCount));
        OnPropertyChanged(nameof(HeaderStatus));
    }
}
