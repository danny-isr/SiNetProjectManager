using SiNet.App.Wpf.Inspection;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailViewerPaneViewModel : ObservableObject
{
    private string _subject = string.Empty;
    private string _sender = string.Empty;
    private string _receivedDisplay = string.Empty;
    private string _bodyText = string.Empty;
    private string _accStatusDisplay = string.Empty;

    public string Subject
    {
        get => _subject;
        set => SetField(ref _subject, value);
    }

    public string Sender
    {
        get => _sender;
        set => SetField(ref _sender, value);
    }

    public string ReceivedDisplay
    {
        get => _receivedDisplay;
        set => SetField(ref _receivedDisplay, value);
    }

    public string BodyText
    {
        get => _bodyText;
        set => SetField(ref _bodyText, value);
    }

    public string AccStatusDisplay
    {
        get => _accStatusDisplay;
        set
        {
            if (SetField(ref _accStatusDisplay, value))
            {
                OnPropertyChanged(nameof(ShowAccStatus));
            }
        }
    }

    public bool ShowAccStatus => !string.IsNullOrWhiteSpace(AccStatusDisplay);

    public bool UseRichBodyRenderer { get; private set; }

    public void SyncFromBody(string bodyText, IEmailBodyRenderer? bodyRenderer, string? gmailMessageId)
    {
        BodyText = bodyText;
        UseRichBodyRenderer = bodyRenderer?.IsAvailable == true;
        OnPropertyChanged(nameof(UseRichBodyRenderer));

        if (bodyRenderer?.IsAvailable == true && !string.IsNullOrWhiteSpace(gmailMessageId))
        {
            _ = bodyRenderer.LoadAsync(
                new EmailBodyRenderRequest(bodyText, HtmlBody: null, gmailMessageId),
                CancellationToken.None);
        }
    }

    public void Clear()
    {
        Subject = string.Empty;
        Sender = string.Empty;
        ReceivedDisplay = string.Empty;
        BodyText = string.Empty;
        AccStatusDisplay = string.Empty;
        UseRichBodyRenderer = false;
        OnPropertyChanged(nameof(UseRichBodyRenderer));
    }
}
