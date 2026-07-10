using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailExternalDownloadLinkItem : ObservableObject
{
    public EmailExternalDownloadLinkItem(string url, Action<string> openUrl)
    {
        Url = url;
        DisplayLabel = FormatDisplayLabel(url);
        OpenCommand = new RelayCommand(_ => openUrl(url));
    }

    public string Url { get; }
    public string DisplayLabel { get; }
    public ICommand OpenCommand { get; }

    private static string FormatDisplayLabel(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Length <= 48 ? url : url[..45] + "...";
        }

        var host = uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length > 28)
        {
            path = path[..25] + "...";
        }

        return string.IsNullOrEmpty(path) || path == "/"
            ? host
            : $"{host}{path}";
    }
}

public sealed class EmailAttachmentStripViewModel : ObservableObject
{
    private readonly Action<string> _openExternalDownloadLink;
    private bool _hasExternalDownloadLinks;

    public EmailAttachmentStripViewModel(Action<string> openExternalDownloadLink)
    {
        _openExternalDownloadLink = openExternalDownloadLink;
        Attachments = [];
        ExternalDownloadLinks = [];
    }

    public ObservableCollection<EmailDetailAttachmentItem> Attachments { get; }

    public ObservableCollection<EmailExternalDownloadLinkItem> ExternalDownloadLinks { get; }

    public bool HasExternalDownloadLinks
    {
        get => _hasExternalDownloadLinks;
        private set => SetField(ref _hasExternalDownloadLinks, value);
    }

    public void SetExternalDownloadLinks(IReadOnlyList<string> urls)
    {
        ExternalDownloadLinks.Clear();
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ExternalDownloadLinks.Add(new EmailExternalDownloadLinkItem(url, _openExternalDownloadLink));
        }

        HasExternalDownloadLinks = ExternalDownloadLinks.Count > 0;
    }

    public void Clear()
    {
        Attachments.Clear();
        ExternalDownloadLinks.Clear();
        HasExternalDownloadLinks = false;
    }
}
