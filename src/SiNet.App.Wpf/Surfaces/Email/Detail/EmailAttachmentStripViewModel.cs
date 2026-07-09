using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailAttachmentStripViewModel : ObservableObject
{
    private bool _showExternalDownloadLinkAction;

    public EmailAttachmentStripViewModel(Action openExternalDownloadLink)
    {
        Attachments = [];
        TaggingAttachments = [];
        OpenExternalDownloadLinkCommand = new RelayCommand(
            _ => openExternalDownloadLink(),
            _ => ShowExternalDownloadLinkAction);
    }

    public ObservableCollection<EmailAttachmentRow> Attachments { get; }

    public ObservableCollection<EmailDetailAttachmentItem> TaggingAttachments { get; }

    public bool HasTaggingAttachments => TaggingAttachments.Count > 0;

    public bool ShowExternalDownloadLinkAction
    {
        get => _showExternalDownloadLinkAction;
        set
        {
            if (SetField(ref _showExternalDownloadLinkAction, value))
            {
                (OpenExternalDownloadLinkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand OpenExternalDownloadLinkCommand { get; }

    public void Clear()
    {
        Attachments.Clear();
        TaggingAttachments.Clear();
        NotifyTaggingAttachmentsChanged();
    }

    public void NotifyTaggingAttachmentsChanged() => OnPropertyChanged(nameof(HasTaggingAttachments));
}
