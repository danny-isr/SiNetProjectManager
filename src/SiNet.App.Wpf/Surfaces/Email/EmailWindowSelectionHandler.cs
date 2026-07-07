using System.Collections.ObjectModel;
using System.IO;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.App.Wpf.Surfaces.Email;

internal sealed class EmailWindowSelectionHandler
{
    private readonly IEmailGateway _emailGateway;
    private readonly EmailListViewModel _emailList;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<string> _setSelectedEmailBody;
    private readonly Action<string> _setSelectedAccStatusDisplay;
    private readonly ObservableCollection<EmailAttachmentRow> _attachments;
    private readonly Func<EmailListRow?> _getSelectedEmail;
    private readonly Func<int> _getLoadVersion;
    private readonly Action _bumpLoadVersion;

    public EmailWindowSelectionHandler(
        IEmailGateway emailGateway,
        EmailListViewModel emailList,
        Action<string> setStatusMessage,
        Action<string> setSelectedEmailBody,
        Action<string> setSelectedAccStatusDisplay,
        ObservableCollection<EmailAttachmentRow> attachments,
        Func<EmailListRow?> getSelectedEmail,
        Func<int> getLoadVersion,
        Action bumpLoadVersion)
    {
        _emailGateway = emailGateway;
        _emailList = emailList;
        _setStatusMessage = setStatusMessage;
        _setSelectedEmailBody = setSelectedEmailBody;
        _setSelectedAccStatusDisplay = setSelectedAccStatusDisplay;
        _attachments = attachments;
        _getSelectedEmail = getSelectedEmail;
        _getLoadVersion = getLoadVersion;
        _bumpLoadVersion = bumpLoadVersion;
    }

    public void ClearSelectedEmailDetails()
    {
        _bumpLoadVersion();
        _setSelectedEmailBody(string.Empty);
        _setSelectedAccStatusDisplay(string.Empty);
        _attachments.Clear();
    }

    public void PrepareSelectedEmailDetailsLoading()
    {
        _setSelectedEmailBody("טוען תוכן מייל...");
        _attachments.Clear();
        var selected = _getSelectedEmail();
        if (selected?.HasAttachments == true)
        {
            _attachments.Add(new EmailAttachmentRow(
                "טוען פרטי קבצים...",
                "Loading",
                "..."));
        }
    }

    public async Task LoadSelectedEmailWithAccPipelineAsync(EmailListRow row, int loadVersion)
    {
        await LoadSelectedEmailDetailsAsync(row.Id, loadVersion).ConfigureAwait(true);

        if (!ShouldApplySelectedEmailLoad(row.Id, loadVersion))
        {
            return;
        }

        _setSelectedAccStatusDisplay("בודק ACC…");
        var (_, status) = await _emailList.TryPassiveAccIngestOnSelectionAsync(
            row,
            () => ShouldApplySelectedEmailLoad(row.Id, loadVersion))
            .ConfigureAwait(true);

        if (!ShouldApplySelectedEmailLoad(row.Id, loadVersion))
        {
            return;
        }

        _setSelectedAccStatusDisplay(status?.StatusDisplay
            ?? _getSelectedEmail()?.AccStatusDisplay
            ?? string.Empty);
    }

    public Task OpenSelectedEmailAsync()
    {
        var selected = _getSelectedEmail();
        if (selected is null)
        {
            _setStatusMessage("לא נבחר מייל.");
            return Task.CompletedTask;
        }

        _bumpLoadVersion();
        var loadVersion = _getLoadVersion();
        PrepareSelectedEmailDetailsLoading();
        return LoadSelectedEmailDetailsAsync(selected.Id, loadVersion);
    }

    public async Task LoadSelectedEmailDetailsAsync(string messageId, int loadVersion)
    {
        try
        {
            var details = await _emailGateway.GetDetailsAsync(messageId).ConfigureAwait(true);
            if (!ShouldApplySelectedEmailLoad(messageId, loadVersion))
            {
                return;
            }

            if (details is null)
            {
                ApplyMissingSelectedEmailDetails();
                _setStatusMessage("לא ניתן היה לטעון את תוכן המייל המלא.");
                return;
            }

            ApplySelectedEmailDetails(details);
            _setStatusMessage(details.HasAttachments
                ? $"נטען תוכן המייל ו-{details.Attachments.Count} קבצים מצורפים."
                : "נטען תוכן המייל המלא.");
        }
        catch (Exception ex)
        {
            if (!ShouldApplySelectedEmailLoad(messageId, loadVersion))
            {
                return;
            }

            ApplyMissingSelectedEmailDetails();
            _setStatusMessage($"טעינת תוכן המייל נכשלה: {ex.Message}");
        }
    }

    private bool ShouldApplySelectedEmailLoad(string messageId, int loadVersion) =>
        loadVersion == _getLoadVersion()
        && string.Equals(_getSelectedEmail()?.Id, messageId, StringComparison.Ordinal);

    private void ApplySelectedEmailDetails(EmailMessageDetails details)
    {
        _setSelectedEmailBody(string.IsNullOrWhiteSpace(details.BodyText)
            ? "לא התקבל תוכן טקסטואלי זמין עבור המייל הזה."
            : details.BodyText);

        _attachments.Clear();
        foreach (var attachment in details.Attachments)
        {
            _attachments.Add(new EmailAttachmentRow(
                attachment.FileName,
                FormatAttachmentKind(attachment),
                FormatAttachmentSize(attachment.SizeBytes)));
        }
    }

    private void ApplyMissingSelectedEmailDetails()
    {
        var selected = _getSelectedEmail();
        _setSelectedEmailBody(selected is null
            ? string.Empty
            : $"לא ניתן היה לטעון את תוכן המייל המלא.\n\nשולח: {selected.Sender}\nנושא: {selected.Subject}\nהתקבל: {selected.ReceivedDisplay}");

        _attachments.Clear();
        if (selected?.HasAttachments == true)
        {
            _attachments.Add(new EmailAttachmentRow(
                "פרטי הקבצים לא זמינים כרגע",
                "Unavailable",
                "..."));
        }
    }

    private static string FormatAttachmentKind(EmailMessageAttachmentDetails attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(attachment.ContentType) ? "FILE" : attachment.ContentType;
    }

    private static string FormatAttachmentSize(long? sizeBytes)
    {
        if (sizeBytes is null || sizeBytes <= 0)
        {
            return "Unknown";
        }

        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        if (sizeBytes >= megabyte)
        {
            return $"{sizeBytes.Value / megabyte:0.#} MB";
        }

        if (sizeBytes >= kilobyte)
        {
            return $"{sizeBytes.Value / kilobyte:0.#} KB";
        }

        return $"{sizeBytes.Value} B";
    }
}
