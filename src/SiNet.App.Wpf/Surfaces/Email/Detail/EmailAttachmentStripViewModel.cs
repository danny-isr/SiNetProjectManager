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
        OpenExternalDownloadLinkCommand = new RelayCommand(
            _ => openExternalDownloadLink(),
            _ => ShowExternalDownloadLinkAction);
    }

    public ObservableCollection<EmailAttachmentRow> Attachments { get; }

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

    public void Clear() => Attachments.Clear();
}
