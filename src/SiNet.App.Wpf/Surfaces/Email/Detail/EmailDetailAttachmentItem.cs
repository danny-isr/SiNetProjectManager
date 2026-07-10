using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailDetailAttachmentItem : ObservableObject
{
    private int _inboxAttachmentId;
    private bool _isTaggable;
    private int? _selectedAlternativeId;
    private string? _taggedProjectFileTitle;
    private int? _previousAlternativeId;

    public EmailDetailAttachmentItem(
        int inboxAttachmentId,
        string fileName,
        string kind,
        string size,
        bool isTaggable,
        Func<EmailDetailAttachmentItem, Task> tagAsync,
        Func<EmailDetailAttachmentItem, Task> alternativeChangedAsync)
    {
        _inboxAttachmentId = inboxAttachmentId;
        FileName = fileName;
        Kind = kind;
        Size = size;
        _isTaggable = isTaggable;
        AvailableAlternatives = [];
        TagCommand = new AsyncRelayCommand(
            () => tagAsync(this),
            () => IsTaggable && CanEditTarget && InboxAttachmentId > 0);
        AlternativeChangedCommand = new AsyncRelayCommand(
            () => alternativeChangedAsync(this),
            () => IsTaggable && CanEditTarget && ShowAlternativeSelector);
    }

    public int InboxAttachmentId => _inboxAttachmentId;
    public string FileName { get; }
    public string Kind { get; }
    public string Size { get; }

    public bool IsTaggable
    {
        get => _isTaggable;
        private set
        {
            if (SetField(ref _isTaggable, value))
            {
                OnPropertyChanged(nameof(ShowTagSelector));
                OnPropertyChanged(nameof(ShowAlternativeSelector));
                OnPropertyChanged(nameof(CanEditTarget));
                (TagCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<EmailProjectAlternativeOption> AvailableAlternatives { get; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Size)
        ? $"{FileName}  ({Kind})"
        : $"{FileName}  ({Kind}, {Size})";

    public bool IsTagged => ProjectFileId is > 0;

    public bool ShowTagSelector => IsTaggable && InboxAttachmentId > 0;

    public bool ShowAlternativeSelector =>
        IsTaggable && InboxAttachmentId > 0
        && AvailableAlternatives.Any(static a => !a.IsCreateNew);

    public bool CanEditTarget => IsTaggable && InboxAttachmentId > 0;

    public int? ProjectFileId { get; private set; }

    public string? TaggedProjectFileTitle
    {
        get => _taggedProjectFileTitle;
        private set
        {
            if (SetField(ref _taggedProjectFileTitle, value))
            {
                OnPropertyChanged(nameof(IsTagged));
                OnPropertyChanged(nameof(TagButtonText));
            }
        }
    }

    public string TagButtonText => IsTagged
        ? $"🔗 {TaggedProjectFileTitle}"
        : "🔗 בחר קובץ";

    public int? SelectedAlternativeId
    {
        get => _selectedAlternativeId;
        set
        {
            if (SetField(ref _selectedAlternativeId, value))
            {
                (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand TagCommand { get; }
    public ICommand AlternativeChangedCommand { get; }

    public void ApplyTag(int? projectFileId, string? projectFileTitle, int? projectAlternativeId)
    {
        ProjectFileId = projectFileId is > 0 ? projectFileId : null;
        TaggedProjectFileTitle = projectFileId is > 0 ? projectFileTitle : null;
        if (projectAlternativeId is > 0)
        {
            _previousAlternativeId = projectAlternativeId;
            SelectedAlternativeId = projectAlternativeId;
        }

        (TagCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsTagged));
        OnPropertyChanged(nameof(TagButtonText));
    }

    public void ApplyInboxTagState(
        int inboxAttachmentId,
        bool isTaggable,
        int? projectFileId,
        string? projectFileTitle,
        int? projectAlternativeId,
        IReadOnlyList<EmailProjectAlternativeOption> alternatives)
    {
        _inboxAttachmentId = inboxAttachmentId;
        OnPropertyChanged(nameof(InboxAttachmentId));
        IsTaggable = isTaggable;
        SetAlternatives(alternatives);
        ApplyTag(
            projectFileId,
            projectFileTitle,
            projectAlternativeId
            ?? alternatives.FirstOrDefault(a => a.IsDefault && !a.IsCreateNew)?.Id);
        OnPropertyChanged(nameof(ShowTagSelector));
        OnPropertyChanged(nameof(ShowAlternativeSelector));
        OnPropertyChanged(nameof(CanEditTarget));
    }

    public void SetAlternatives(IReadOnlyList<EmailProjectAlternativeOption> alternatives)
    {
        AvailableAlternatives.Clear();
        foreach (var option in alternatives.Where(static a => !a.IsCreateNew))
        {
            AvailableAlternatives.Add(option);
        }

        if (IsTaggable && AvailableAlternatives.Count > 0)
        {
            AvailableAlternatives.Add(EmailProjectAlternativeOption.CreateNewSentinel);
        }

        OnPropertyChanged(nameof(ShowAlternativeSelector));
        (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public void RestorePreviousAlternativeSelection()
    {
        SelectedAlternativeId = _previousAlternativeId
            ?? AvailableAlternatives.FirstOrDefault(a => a.IsDefault && !a.IsCreateNew)?.Id;
    }

    public void RememberCurrentAlternativeAsPrevious()
    {
        if (SelectedAlternativeId is > 0)
        {
            _previousAlternativeId = SelectedAlternativeId;
        }
    }
}
