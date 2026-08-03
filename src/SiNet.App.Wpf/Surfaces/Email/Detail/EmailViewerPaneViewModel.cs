using SiNet.App.Wpf.Inspection;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailViewerPaneViewModel : ObservableObject
{
    private readonly Action<string>? _openBodyLink;
    private string _subject = string.Empty;
    private string _sender = string.Empty;
    private string _receivedDisplay = string.Empty;
    private string _bodyText = string.Empty;
    private string? _htmlBody;
    private string? _gmailMessageId;
    private IReadOnlyList<SiNet.Application.Abstractions.Email.EmailInlineImage> _inlineImages = [];
    private string _accStatusDisplay = string.Empty;
    private IEmailBodyRenderer? _bodyRenderer;

    /// <param name="openBodyLink">
    /// Receives link clicks the renderer refused to follow in place (DEV-001). Owned by
    /// <c>EmailDetailViewModel</c> so body links and attachment-strip chips share one open path.
    /// </param>
    public EmailViewerPaneViewModel(Action<string>? openBodyLink = null)
    {
        _openBodyLink = openBodyLink;
    }

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
        AttachRenderer(bodyRenderer);
        _ = TryRenderRichBodyAsync();
    }

    public void SyncFromBody(
        string bodyText,
        string? htmlBody,
        IEmailBodyRenderer? bodyRenderer,
        string? gmailMessageId,
        IReadOnlyList<SiNet.Application.Abstractions.Email.EmailInlineImage>? inlineImages = null)
    {
        BodyText = bodyText ?? string.Empty;
        _htmlBody = htmlBody;
        _gmailMessageId = gmailMessageId;
        _inlineImages = inlineImages ?? [];
        // Keep the renderer that received AttachHost via SetBodyRenderer.
        // EmailDetailViewModel may hold a different Transient DI instance — overwriting it
        // leaves LoadAsync on an unattached WebView2 (deferred forever → no inline images).
        if (bodyRenderer is not null && _bodyRenderer is null)
        {
            AttachRenderer(bodyRenderer);
        }

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
        _inlineImages = [];
        AccStatusDisplay = string.Empty;
        UseRichBodyRenderer = false;
        OnPropertyChanged(nameof(UseRichBodyRenderer));
        _bodyRenderer?.Clear();
    }

    private void AttachRenderer(IEmailBodyRenderer? bodyRenderer)
    {
        if (ReferenceEquals(_bodyRenderer, bodyRenderer))
        {
            return;
        }

        if (_bodyRenderer is not null)
        {
            _bodyRenderer.ExternalLinkRequested -= OnExternalLinkRequested;
        }

        _bodyRenderer = bodyRenderer;

        if (_bodyRenderer is not null)
        {
            _bodyRenderer.ExternalLinkRequested += OnExternalLinkRequested;
        }
    }

    private void OnExternalLinkRequested(string url) => _openBodyLink?.Invoke(url);

    private async Task TryRenderRichBodyAsync()
    {
        // Prefer plain text unless we have HTML and a working renderer — avoids blank WebView hiding body.
        var hasRenderer = _bodyRenderer?.IsAvailable == true;
        var hasGmailId = !string.IsNullOrWhiteSpace(_gmailMessageId);
        var hasHtml = !string.IsNullOrWhiteSpace(_htmlBody);
        var hasBody = !string.IsNullOrWhiteSpace(BodyText) && BodyText != "טוען תוכן מייל...";

        if (!hasRenderer || !hasGmailId || !hasHtml || !hasBody)
        {
            if (UseRichBodyRenderer)
            {
                UseRichBodyRenderer = false;
                OnPropertyChanged(nameof(UseRichBodyRenderer));
            }

            // Blank the previous email's HTML so it can never linger behind/over the
            // plain-text fallback when this email has no rich body.
            if (hasRenderer && hasGmailId)
            {
                _bodyRenderer!.Clear();
            }

            return;
        }

        var messageId = _gmailMessageId;
        var bodySnapshot = BodyText;
        var loaded = await _bodyRenderer!.LoadAsync(
            new EmailBodyRenderRequest(BodyText, _htmlBody, _gmailMessageId, _inlineImages),
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
