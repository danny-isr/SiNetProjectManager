using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailDetailAttachmentItem : ObservableObject
{
    private int _inboxAttachmentId;
    private bool _isTaggable;
    private int? _selectedAlternativeId;
    private string? _taggedProjectFileTitle;
    private int? _previousAlternativeId;
    private string? _accItemId;

    public EmailDetailAttachmentItem(
        int inboxAttachmentId,
        string fileName,
        string kind,
        string size,
        bool isTaggable,
        Func<EmailDetailAttachmentItem, Task> tagAsync,
        Func<EmailDetailAttachmentItem, Task> alternativeChangedAsync,
        Func<EmailDetailAttachmentItem, Task>? openInAccAsync = null)
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
        OpenInAccCommand = new AsyncRelayCommand(
            () => openInAccAsync is null ? Task.CompletedTask : openInAccAsync(this),
            () => CanOpenInAcc && openInAccAsync is not null);
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

    public string DisplayLabel
    {
        get
        {
            var displayName = string.Equals(FileName, "00_Email.pdf", StringComparison.OrdinalIgnoreCase)
                ? "תוכן המייל (PDF)"
                : FileName;
            return string.IsNullOrWhiteSpace(Size)
                ? $"{displayName}  ({Kind})"
                : $"{displayName}  ({Kind}, {Size})";
        }
    }

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
    public ICommand OpenInAccCommand { get; }

    public string? AccItemId
    {
        get => _accItemId;
        private set
        {
            if (SetField(ref _accItemId, value))
            {
                OnPropertyChanged(nameof(CanOpenInAcc));
                (OpenInAccCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanOpenInAcc => !string.IsNullOrWhiteSpace(AccItemId);

    public void ApplyTag(int? projectFileId, string? projectFileTitle, int? projectAlternativeId)
    {
        ProjectFileId = projectFileId is > 0 ? projectFileId : null;
        TaggedProjectFileTitle = projectFileId is > 0 ? projectFileTitle : null;

        var resolvedAlternativeId = projectAlternativeId is > 0
            ? projectAlternativeId
            : EmailProjectAlternativeOption.ResolveDefaultId(AvailableAlternatives);

        if (resolvedAlternativeId is > 0)
        {
            _previousAlternativeId = resolvedAlternativeId;
            SelectedAlternativeId = resolvedAlternativeId;
        }
        else if (ProjectFileId is null)
        {
            SelectedAlternativeId = null;
        }

        // #region agent log
        AgentDebugLog(
            "H-ALT1",
            "EmailDetailAttachmentItem.ApplyTag",
            $"att={InboxAttachmentId} pf={ProjectFileId} title='{TaggedProjectFileTitle}' inAlt={projectAlternativeId?.ToString() ?? "null"} resolved={resolvedAlternativeId?.ToString() ?? "null"} selected={SelectedAlternativeId?.ToString() ?? "null"} alts={AvailableAlternatives.Count}");
        // #endregion

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
        IReadOnlyList<EmailProjectAlternativeOption> alternatives,
        string? accItemId = null)
    {
        _inboxAttachmentId = inboxAttachmentId;
        OnPropertyChanged(nameof(InboxAttachmentId));
        IsTaggable = isTaggable;
        AccItemId = accItemId;
        SetAlternatives(alternatives);
        ApplyTag(
            projectFileId,
            projectFileTitle,
            projectAlternativeId
            ?? EmailProjectAlternativeOption.ResolveDefaultId(alternatives));
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

        // After alternatives appear (e.g. project remembered, tags restored), ensure a selection.
        var beforeSelect = SelectedAlternativeId;
        var fallback = EmailProjectAlternativeOption.ResolveDefaultId(AvailableAlternatives);
        var appliedFallback = false;
        if (IsTagged && SelectedAlternativeId is not > 0)
        {
            if (fallback is > 0)
            {
                SelectedAlternativeId = fallback;
                _previousAlternativeId = fallback;
                appliedFallback = true;
            }
        }

        // #region agent log
        AgentDebugLog(
            "H-ALT1",
            "EmailDetailAttachmentItem.SetAlternatives",
            $"att={InboxAttachmentId} isTagged={IsTagged} before={beforeSelect?.ToString() ?? "null"} after={SelectedAlternativeId?.ToString() ?? "null"} fallback={fallback?.ToString() ?? "null"} appliedFallback={appliedFallback} alts={AvailableAlternatives.Count} names=[{string.Join(",", AvailableAlternatives.Where(a => !a.IsCreateNew).Select(a => $"{a.Id}:{a.Name}:def={a.IsDefault}"))}]");
        // #endregion

        OnPropertyChanged(nameof(ShowAlternativeSelector));
        (AlternativeChangedCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static void AgentDebugLog(string hypothesisId, string location, string detail)
        => WorkflowDebugTrace.Step("Email.TagUI", $"{hypothesisId} {location} {detail}");

    public void RestorePreviousAlternativeSelection()
    {
        SelectedAlternativeId = _previousAlternativeId
            ?? EmailProjectAlternativeOption.ResolveDefaultId(AvailableAlternatives);
    }

    public void RememberCurrentAlternativeAsPrevious()
    {
        if (SelectedAlternativeId is > 0)
        {
            _previousAlternativeId = SelectedAlternativeId;
        }
    }
}
