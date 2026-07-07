using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Collapsible label group in group-by-label mode with per-label Gmail paging.</summary>
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
        Func<EmailLabelGroupViewModel, Task> loadAll)
    {
        LabelId = labelId ?? throw new ArgumentNullException(nameof(labelId));
        LabelDisplayName = labelDisplayName ?? throw new ArgumentNullException(nameof(labelDisplayName));
        _loadMore = loadMore ?? throw new ArgumentNullException(nameof(loadMore));
        _loadAll = loadAll ?? throw new ArgumentNullException(nameof(loadAll));

        Emails = [];

        ToggleExpandCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ExpandCommand = new RelayCommand(_ => IsExpanded = true);
        CollapseCommand = new RelayCommand(_ => IsExpanded = false);
        LoadMoreForLabelCommand = new AsyncRelayCommand(() => _loadMore(this), () => !IsLoading && HasMore);
        LoadAllForLabelCommand = new AsyncRelayCommand(() => _loadAll(this), () => !IsLoading);
    }

    public string LabelId { get; }

    public string LabelDisplayName { get; }

    public ObservableCollection<EmailListRow> Emails { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        internal set
        {
            if (SetField(ref _isLoading, value))
            {
                NotifyHeaderChanged();
                (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (LoadAllForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasLoadedAll
    {
        get => _hasLoadedAll;
        internal set
        {
            if (SetField(ref _hasLoadedAll, value))
            {
                NotifyHeaderChanged();
                (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public int LoadedCount => Emails.Count;

    public bool HasMore
    {
        get => _hasMore;
        internal set
        {
            if (SetField(ref _hasMore, value))
            {
                NotifyHeaderChanged();
                (LoadMoreForLabelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string? NextPageToken
    {
        get => _nextPageToken;
        internal set => _nextPageToken = value;
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(ShowGroupError));
            }
        }
    }

    public bool ShowGroupError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string HeaderStatus
    {
        get
        {
            if (IsLoading)
            {
                return $"{LabelDisplayName} — טוען...";
            }

            var status = $"{LabelDisplayName} — {LoadedCount} נטענו";
            if (HasLoadedAll)
            {
                return $"{status} — הכול נטען";
            }

            if (HasMore)
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
        if (!_seenMessageIds.Add(row.Id))
        {
            return false;
        }

        Emails.Add(row);
        NotifyHeaderChanged();
        return true;
    }

    internal void ResetPagingState()
    {
        _nextPageToken = null;
        _hasMore = true;
        _hasLoadedAll = false;
        _errorMessage = null;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ShowGroupError));
        NotifyHeaderChanged();
    }

    internal void ClearEmails()
    {
        Emails.Clear();
        _seenMessageIds.Clear();
        ResetPagingState();
    }

    internal void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(LoadedCount));
        OnPropertyChanged(nameof(HeaderStatus));
    }
}
