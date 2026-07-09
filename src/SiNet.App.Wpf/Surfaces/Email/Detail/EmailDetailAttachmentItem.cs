using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailDetailAttachmentItem : ObservableObject
{
    private int? _selectedAlternativeId;
    private string? _taggedProjectFileTitle;

    public EmailDetailAttachmentItem(
        int inboxAttachmentId,
        string fileName,
        string kind,
        string size,
        bool isTaggable,
        Func<EmailDetailAttachmentItem, Task> tagAsync,
        Func<EmailDetailAttachmentItem, Task> alternativeChangedAsync)
    {
        InboxAttachmentId = inboxAttachmentId;
        FileName = fileName;
        Kind = kind;
        Size = size;
        IsTaggable = isTaggable;
        AvailableAlternatives = [];
        TagCommand = new AsyncRelayCommand(
            () => tagAsync(this),
            () => IsTaggable && CanEditTarget);
        AlternativeChangedCommand = new AsyncRelayCommand(
            () => alternativeChangedAsync(this),
            () => IsTaggable && CanEditTarget && ShowAlternativeSelector);
    }

    public int InboxAttachmentId { get; }
    public string FileName { get; }
    public string Kind { get; }
    public string Size { get; }
    public bool IsTaggable { get; }

    public ObservableCollection<EmailProjectAlternativeOption> AvailableAlternatives { get; }

    public string DisplayLabel => $"{FileName}  ({Kind}, {Size})";

    public bool IsTagged => ProjectFileId is > 0;

    public bool ShowTagSelector => IsTaggable;

    public bool ShowAlternativeSelector => IsTaggable && AvailableAlternatives.Count > 1;

    public bool CanEditTarget => IsTaggable;

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
        SelectedAlternativeId = projectAlternativeId;
        (TagCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsTagged));
        OnPropertyChanged(nameof(TagButtonText));
    }

    public void SetAlternatives(IReadOnlyList<EmailProjectAlternativeOption> alternatives)
    {
        AvailableAlternatives.Clear();
        foreach (var option in alternatives)
        {
            AvailableAlternatives.Add(option);
        }

        OnPropertyChanged(nameof(ShowAlternativeSelector));
        (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}
