using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.Application.Email.Acc;



namespace SiNet.App.Wpf.Surfaces.Email;



/// <summary>

/// Handles external download completion (JumboMail / WeTransfer) → ACC upload + attachment list refresh.

/// </summary>

internal sealed class EmailExternalDownloadHandler

{

    private readonly IEmailExternalDownloadCoordinator? _coordinator;

    private readonly IEmailExternalDownloadBrowserHost? _browserHost;

    private readonly IEmailAccBackgroundWorkTracker? _backgroundWorkTracker;

    private readonly EmailListViewModel _emailList;

    private readonly EmailDetailSelectionCoordinator _selectionCoordinator;

    private readonly Action<string> _setStatusMessage;

    private readonly Func<EmailListRow?> _getSelectedEmail;

    private readonly Func<int> _getLoadVersion;

    private readonly Action _bumpLoadVersion;



    public EmailExternalDownloadHandler(

        IEmailExternalDownloadCoordinator? coordinator,

        IEmailExternalDownloadBrowserHost? browserHost,

        IEmailAccBackgroundWorkTracker? backgroundWorkTracker,

        EmailListViewModel emailList,

        EmailDetailSelectionCoordinator selectionHandler,

        Action<string> setStatusMessage,

        Func<EmailListRow?> getSelectedEmail,

        Func<int> getLoadVersion,

        Action bumpLoadVersion)

    {

        _coordinator = coordinator;

        _browserHost = browserHost;

        _backgroundWorkTracker = backgroundWorkTracker;

        _emailList = emailList;

        _selectionCoordinator = selectionHandler;

        _setStatusMessage = setStatusMessage;

        _getSelectedEmail = getSelectedEmail;

        _getLoadVersion = getLoadVersion;

        _bumpLoadVersion = bumpLoadVersion;



        if (_browserHost is not null)

        {

            _browserHost.DownloadCompleted += OnDownloadCompleted;

        }

    }



    public bool IsAvailable => _coordinator is not null && _browserHost is not null;



    public void OpenDownloadLink(string url, EmailListRow row)
    {
        if (_browserHost is null)
        {
            _setStatusMessage("פתיחת קישור הורדה אינה זמינה.");
            return;
        }

        if (string.IsNullOrWhiteSpace(url)
            || !EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url))
        {
            _setStatusMessage("קישור ההורדה אינו תקין.");
            return;
        }

        var context = BuildContext(row);
        _setStatusMessage($"פותח קישור הורדה… ({url})");
        _browserHost.OpenDownloadUrl(url, context);
    }

    public void OpenFirstDownloadLink(string bodyText, EmailListRow row)
    {
        var urls = EmailExternalDownloadLinkDetector.ExtractUrls(bodyText);
        if (urls.Count == 0)
        {
            _setStatusMessage("לא נמצא קישור JumboMail / WeTransfer בתוכן המייל.");
            return;
        }

        OpenDownloadLink(urls[0], row);
    }



    public async Task MergeExternalDownloadsIntoViewerAsync(

        EmailListRow row,

        int loadVersion,

        CancellationToken cancellationToken = default)

    {

        if (_coordinator is null)

        {

            return;

        }



        var external = await _coordinator

            .ListExternalDownloadsAsync(row.InternetMessageId, row.Id, cancellationToken)

            .ConfigureAwait(true);



        if (external.Count == 0)

        {

            return;

        }



        if (!IsStillSelected(row.Id, loadVersion))

        {

            return;

        }



        _selectionCoordinator.MergeExternalDownloadAttachments(external);

    }



    public void Dispose()

    {

        if (_browserHost is not null)

        {

            _browserHost.DownloadCompleted -= OnDownloadCompleted;

        }

    }



    private async void OnDownloadCompleted(EmailExternalDownloadCompletedEventArgs args)

    {

        if (_coordinator is null)

        {

            return;

        }



        using var _ = _backgroundWorkTracker?.BeginWork();

        try

        {

            _setStatusMessage($"מעלה {args.FileName} ל-ACC Inbox…");

            var command = new EmailExternalDownloadCommand(
                args.Context.GmailMessageId,
                args.Context.InternetMessageId,
                args.LocalFilePath,
                args.FileName,
                args.Context.Subject,
                args.Context.From,
                args.Context.ReceivedOn,
                ResolveActingUserLogin());

            var progress = new Progress<EmailExternalDownloadProgress>(p =>
            {
                _browserHost?.ReportProgress(p);
                _setStatusMessage(p.Message);
            });

            var result = await _coordinator.UploadExternalFileAsync(command, progress).ConfigureAwait(true);

            if (result.Succeeded)
            {
                _setStatusMessage($"הועלה {result.FileName ?? args.FileName} ל-ACC Inbox");
                _browserHost?.ReportProgress(new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Completed,
                    $"הועלה {result.FileName ?? args.FileName} ל-ACC Inbox",
                    Percent: 100,
                    FileName: result.FileName ?? args.FileName));
            }
            else
            {
                var error = result.ErrorMessage ?? "העלאת הקובץ החיצוני ל-ACC נכשלה";
                _setStatusMessage(error);
                _browserHost?.ReportProgress(new EmailExternalDownloadProgress(
                    EmailExternalDownloadStage.Failed,
                    error,
                    FileName: args.FileName));
            }



            var selected = _getSelectedEmail();

            if (selected is null

                || !string.Equals(selected.Id, args.Context.GmailMessageId, StringComparison.Ordinal))

            {

                return;

            }



            _bumpLoadVersion();

            var loadVersion = _getLoadVersion();

            await _selectionCoordinator.RunAccPipelineAsync(selected, loadVersion).ConfigureAwait(true);

            await MergeExternalDownloadsIntoViewerAsync(selected, loadVersion).ConfigureAwait(true);

        }

        catch (Exception ex)

        {

            _setStatusMessage($"שגיאה בהעלאת קובץ חיצוני: {ex.Message}");

        }

    }



    private bool IsStillSelected(string messageId, int loadVersion) =>

        loadVersion == _getLoadVersion()

        && string.Equals(_getSelectedEmail()?.Id, messageId, StringComparison.Ordinal);



    private static EmailExternalDownloadContext BuildContext(EmailListRow row) =>

        new(

            row.Id,

            row.InternetMessageId,

            row.Subject,

            row.Sender,

            row.ReceivedOn);



    private static string ResolveActingUserLogin()

    {

        try

        {

            return Environment.UserDomainName + "\\" + Environment.UserName;

        }

        catch

        {

            return Environment.UserName;

        }

    }

}


