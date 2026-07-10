using SiNet.App.Wpf.Inspection;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailViewerPaneViewModel : ObservableObject
{
    private string _subject = string.Empty;
    private string _sender = string.Empty;
    private string _receivedDisplay = string.Empty;
    private string _bodyText = string.Empty;
    private string? _htmlBody;
    private string? _gmailMessageId;
    private string _accStatusDisplay = string.Empty;
    private IEmailBodyRenderer? _bodyRenderer;

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

    public void SetBodyRenderer(IEmailBodyRenderer? bodyRenderer)
    {
        _bodyRenderer = bodyRenderer;
        _ = TryRenderRichBodyAsync();
    }

    public void SyncFromBody(
        string bodyText,
        string? htmlBody,
        IEmailBodyRenderer? bodyRenderer,
        string? gmailMessageId)
    {
        BodyText = bodyText;
        _htmlBody = htmlBody;
        _gmailMessageId = gmailMessageId;
        _bodyRenderer = bodyRenderer;
        UseRichBodyRenderer = false;
        OnPropertyChanged(nameof(UseRichBodyRenderer));
        _ = TryRenderRichBodyAsync();
    }

    public void Clear()
    {
        Subject = string.Empty;
        Sender = string.Empty;
        ReceivedDisplay = string.Empty;
        BodyText = string.Empty;
        _htmlBody = null;
        _gmailMessageId = null;
        AccStatusDisplay = string.Empty;
        UseRichBodyRenderer = false;
        OnPropertyChanged(nameof(UseRichBodyRenderer));
    }

    private async Task TryRenderRichBodyAsync()
    {
        // Prefer plain text unless we have HTML and a working renderer — avoids blank WebView hiding body.
        if (_bodyRenderer?.IsAvailable != true
            || string.IsNullOrWhiteSpace(_gmailMessageId)
            || string.IsNullOrWhiteSpace(_htmlBody)
            || string.IsNullOrWhiteSpace(BodyText)
            || BodyText == "טוען תוכן מייל...")
        {
            if (UseRichBodyRenderer)
            {
                UseRichBodyRenderer = false;
                OnPropertyChanged(nameof(UseRichBodyRenderer));
            }

            return;
        }

        var messageId = _gmailMessageId;
        var bodySnapshot = BodyText;
        var loaded = await _bodyRenderer.LoadAsync(
            new EmailBodyRenderRequest(BodyText, _htmlBody, _gmailMessageId),
            CancellationToken.None).ConfigureAwait(true);

        if (!loaded
            || !string.Equals(_gmailMessageId, messageId, StringComparison.Ordinal)
            || !string.Equals(BodyText, bodySnapshot, StringComparison.Ordinal))
        {
            return;
        }

        UseRichBodyRenderer = true;
        OnPropertyChanged(nameof(UseRichBodyRenderer));
    }
}
