using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Combined ProjectSetup host: email + ACC attachments + native project-create form,
/// with decline ("לא הצעת מחיר"). Filing / MoveToProject stays in the next stage.
/// </summary>
public partial class OpenQuoteProjectDecisionDialog : Window, INotifyPropertyChanged
{
    public const string NotQuoteRequest = "NotQuoteRequest";
    public const string ProjectOpened = "ProjectOpened";
    public const string ProjectCreatedEvent = "Review.ProjectCreated";
    public const string QuoteClassifiedEvent = "Review.QuoteRequestClassified";

    private readonly WorkSurfaceContext _context;
    private readonly IOpenQuoteProjectDecisionService _decisionService;
    private readonly ProjectCreateDialogViewModel _createVm;
    private readonly IPlaceCatalogService _places;
    private readonly ICompanyCatalogService _companies;
    private readonly IEmailInboxQueryService? _inboxQuery;
    private readonly IAccResolvedDocsUrlLauncher? _accLauncher;
    private readonly IEmailFilingService? _filingService;
    private readonly IEmailGateway? _emailGateway;

    private string _subject = "טוען…";
    private string _fromDisplay = string.Empty;
    private string _dateDisplay = string.Empty;
    private string _statusMessage = string.Empty;
    private string _attachmentsHint = string.Empty;
    private string? _accProjectId;
    private string? _accFolderId;
    private bool _completing;

    public OpenQuoteProjectDecisionDialog(
        WorkSurfaceContext context,
        IOpenQuoteProjectDecisionService decisionService,
        ProjectCreateDialogViewModel createVm,
        IPlaceCatalogService places,
        ICompanyCatalogService companies,
        IEmailInboxQueryService? inboxQuery = null,
        IAccResolvedDocsUrlLauncher? accLauncher = null,
        IEmailFilingService? filingService = null,
        IEmailGateway? emailGateway = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _createVm = createVm ?? throw new ArgumentNullException(nameof(createVm));
        _places = places ?? throw new ArgumentNullException(nameof(places));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _inboxQuery = inboxQuery;
        _accLauncher = accLauncher;
        _filingService = filingService;
        _emailGateway = emailGateway;

        if (_context.PrimaryWorkTargetEntityId is int emailId && emailId > 0)
            _createVm.EmailMessageId = emailId;
        _createVm.PrimaryActionLabel = "פתיחת פרויקט";

        InitializeComponent();
        DataContext = this;
        ProjectFormHost.DataContext = _createVm;

        _createVm.RequestClose += OnCreateRequestClose;
        _createVm.RequestPlacePicker += OnRequestPlacePickerAsync;
        _createVm.RequestCompanyPicker += OnRequestCompanyPickerAsync;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    public ObservableCollection<AttachmentRow> Attachments { get; } = [];

    public string Subject
    {
        get => _subject;
        private set => SetField(ref _subject, value);
    }

    public string FromDisplay
    {
        get => _fromDisplay;
        private set => SetField(ref _fromDisplay, value);
    }

    public string DateDisplay
    {
        get => _dateDisplay;
        private set => SetField(ref _dateDisplay, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string AttachmentsHint
    {
        get => _attachmentsHint;
        private set => SetField(ref _attachmentsHint, value);
    }

    public string PromptHint =>
        _context.TaskId is int taskId
            ? $"משימה #{taskId} — מלא פרטי פרויקט למטה, או סמן שזה לא הצעת מחיר. אפשר לפתוח קבצים ב-ACC לפני האישור."
            : "מלא פרטי פרויקט למטה, או סמן שזה לא הצעת מחיר. אפשר לפתוח קבצים ב-ACC לפני האישור.";

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await LoadEmailAsync().ConfigureAwait(true);
            await _createVm.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _createVm.RequestClose -= OnCreateRequestClose;
        _createVm.RequestPlacePicker -= OnRequestPlacePickerAsync;
        _createVm.RequestCompanyPicker -= OnRequestCompanyPickerAsync;
        _createVm.Dispose();
    }

    private async Task LoadEmailAsync()
    {
        if (_context.PrimaryWorkTargetEntityId is not int emailId || emailId <= 0 || _inboxQuery is null)
        {
            Subject = _context.PrimaryWorkTargetEntityId is int id
                ? $"מייל מקור #{id}"
                : "(אין קישור למייל מקור)";
            AttachmentsHint = "אין מייל מקור — לא ניתן להציג קבצים.";
            return;
        }

        var email = await _inboxQuery.GetByIdAsync(emailId).ConfigureAwait(true);
        if (email is null)
        {
            Subject = $"מייל #{emailId} לא נמצא";
            return;
        }

        Title = $"פתיחת פרויקט הצעת מחיר — {Truncate(email.Subject, 60)}";
        Subject = string.IsNullOrWhiteSpace(email.Subject) ? "(ללא נושא)" : email.Subject!;
        FromDisplay = string.IsNullOrWhiteSpace(email.FromAddress)
            ? "מאת: (לא ידוע)"
            : $"מאת: {email.FromAddress}";
        DateDisplay = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        _accProjectId = email.InboxAccProjectId;
        _accFolderId = email.InboxAccFolderId;

        var attachments = await _inboxQuery.GetAttachmentsAsync(emailId).ConfigureAwait(true);
        Attachments.Clear();
        foreach (var a in attachments)
            Attachments.Add(new AttachmentRow(a));

        if (Attachments.Count == 0)
            AttachmentsHint = "אין קבצים מקושרים למייל זה.";
        else if (Attachments.All(a => !a.CanOpenInAcc))
            AttachmentsHint = "הקבצים עדיין לא זמינים ב-ACC לפתיחה.";
        else if (string.IsNullOrWhiteSpace(_accProjectId))
            AttachmentsHint = "חסר מזהה פרויקט ACC — לא ניתן לפתוח קבצים.";
        else
            AttachmentsHint = "לחיצה על «פתח ב-ACC» פותחת את הקובץ בדפדפן Autodesk.";
    }

    private async void OnCreateRequestClose(bool confirmed)
    {
        if (_completing)
            return;

        if (!confirmed)
        {
            DialogResult = false;
            Close();
            return;
        }

        if (_createVm.CreatedProjectId is not > 0)
        {
            StatusMessage = "יצירת הפרויקט לא החזירה מזהה.";
            return;
        }

        IsEnabled = false;
        _completing = true;

        // The project was created from this specific email, so the Gmail project label is applied
        // here — immediately on creation — instead of waiting for a manual "שייך לפרויקט" in the
        // FileMaterial stage. Best-effort: a Gmail failure must not block the task completion; the
        // FileMaterial stage surface remains the manual retry path.
        if (!string.IsNullOrWhiteSpace(_createVm.CreatedWarningMessage))
        {
            StatusMessage = _createVm.CreatedWarningMessage;
            MessageBox.Show(
                _createVm.CreatedWarningMessage,
                "אזהרה ביצירת פרויקט",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        StatusMessage = $"נוצר פרויקט #{_createVm.CreatedProjectId} — מתייג את המייל לפרויקט…";
        await TryAutoFileEmailToCreatedProjectAsync(_createVm.CreatedProjectId.Value).ConfigureAwait(true);

        StatusMessage = $"נוצר פרויקט #{_createVm.CreatedProjectId} — סוגר משימה…";
        var ok = await CompleteAsync(ProjectCreatedEvent, ProjectOpened).ConfigureAwait(true);
        if (!ok)
        {
            _completing = false;
            IsEnabled = true;
        }
    }

    /// <summary>
    /// Applies the Gmail project label of the newly created project to the originating email.
    /// Resolves the Gmail message id from the inbox row's RFC 2822 Message-ID via an
    /// <c>rfc822msgid:</c> mailbox search (the SQL row does not store the Gmail id).
    /// Never throws — failures leave a status message and are retryable at the FileMaterial stage.
    /// </summary>
    private async Task TryAutoFileEmailToCreatedProjectAsync(int createdProjectId)
    {
        try
        {
            if (_filingService is null
                || _emailGateway is null
                || _inboxQuery is null
                || _context.PrimaryWorkTargetEntityId is not int emailId
                || emailId <= 0)
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                    $"skipped — filing={_filingService is not null} gateway={_emailGateway is not null} inboxQuery={_inboxQuery is not null} emailId={_context.PrimaryWorkTargetEntityId?.ToString() ?? "(none)"}");
                return;
            }

            var inboxRow = await _inboxQuery.GetByIdAsync(emailId).ConfigureAwait(true);
            if (inboxRow is null || string.IsNullOrWhiteSpace(inboxRow.InternetMessageId))
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                    $"skipped — inbox={emailId} rowFound={inboxRow is not null} internetMessageId=missing");
                return;
            }

            var rfc822Term = EmailMailboxQueryComposer.BuildRfc822MessageIdSearchTerm(inboxRow.InternetMessageId);
            var page = await _emailGateway.GetMailboxPageAsync(
                new EmailMailboxQuery
                {
                    FreeText = rfc822Term,
                    MailboxScope = EmailMailboxScope.AllMail,
                    PageSize = 1,
                }).ConfigureAwait(true);

            var summary = page.Items.FirstOrDefault();
            string? gmailMessageId = summary?.MessageId;
            string? gmailThreadId = summary?.ThreadId;

            // Fallback: MessageUniqueId may already be a Gmail API id (non-RFC822).
            if (string.IsNullOrWhiteSpace(gmailMessageId))
            {
                var apiId = EmailMailboxQueryComposer.TryGetGmailApiMessageId(inboxRow.MessageUniqueId);
                if (!string.IsNullOrWhiteSpace(apiId))
                {
                    var byId = await _emailGateway.GetByIdAsync(apiId).ConfigureAwait(true);
                    if (byId is not null)
                    {
                        gmailMessageId = byId.MessageId;
                        gmailThreadId = byId.ThreadId;
                        WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                            $"locate fallback GetById gmailId={gmailMessageId} inbox={emailId}");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(gmailMessageId))
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                    $"FAILED — Gmail message not found (inbox={emailId}) q={rfc822Term} pageItems={page.Items.Count} unique='{inboxRow.MessageUniqueId}'");
                StatusMessage = "המייל לא אותר ב-Gmail — ניתן לשייך ידנית בשלב תיוק החומר.";
                return;
            }

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                $"filing gmailId={gmailMessageId} inbox={emailId} project={createdProjectId}");

            var result = await _filingService.FileToProjectAsync(
                new FileEmailToProjectCommand(
                    TargetProjectId: createdProjectId,
                    ActingUserId: _context.ActingUserId ?? 0,
                    GmailMessageId: gmailMessageId,
                    InboxMessageId: emailId,
                    GmailThreadId: gmailThreadId,
                    InternetMessageId: inboxRow.InternetMessageId)).ConfigureAwait(true);

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.File.AutoOnCreate",
                $"result ok={result.Succeeded} error={result.ErrorMessage ?? "(none)"}");

            if (!result.Succeeded)
            {
                StatusMessage = $"שיוך המייל לפרויקט נכשל: {result.ErrorMessage} — ניתן לשייך ידנית בשלב תיוק החומר.";
            }
        }
        catch (Exception ex)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Email.File.AutoOnCreate", $"EXCEPTION {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"שיוך המייל לפרויקט נכשל: {ex.Message} — ניתן לשייך ידנית בשלב תיוק החומר.";
        }
    }

    private void OpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AttachmentRow row })
            return;

        if (!row.CanOpenInAcc)
        {
            MessageBox.Show("הקובץ עדיין לא הועלה ל-ACC.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_accProjectId))
        {
            MessageBox.Show("מזהה פרויקט ACC לא נמצא למייל זה.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_accLauncher is null)
        {
            MessageBox.Show("שירות פתיחת ACC אינו זמין.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var url = AccResolvedDocsUrlBuilder.Build(
                _accProjectId,
                _accFolderId ?? string.Empty,
                row.AccItemId!);
            _accLauncher.Open(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת הקובץ: {ex.Message}", "שגיאה",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NotQuote_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "לסמן שהמייל אינו בקשת הצעת מחיר ולסגור את התהליך?",
            "לא הצעת מחיר",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        StatusMessage = string.Empty;
        IsEnabled = false;
        _completing = true;
        var ok = await CompleteAsync(QuoteClassifiedEvent, NotQuoteRequest).ConfigureAwait(true);
        if (!ok)
        {
            _completing = false;
            IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task<bool> CompleteAsync(string eventCode, string resultCode)
    {
        if (_context.TaskId is not int taskId || taskId <= 0)
        {
            MessageBox.Show("חסר מזהה משימה.", "הצעת מחיר", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var userId = _context.ActingUserId ?? 0;
        if (userId <= 0)
        {
            MessageBox.Show("חסר משתמש מחובר להשלמת המשימה.", "הצעת מחיר", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            var result = await _decisionService.CompleteDecisionAsync(
                new OpenQuoteProjectDecisionCommand(
                    taskId,
                    userId,
                    eventCode,
                    resultCode),
                CancellationToken.None).ConfigureAwait(true);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "השלמת המשימה נכשלה.",
                    "הצעת מחיר",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            DialogResult = true;
            Close();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה: {ex.Message}", "הצעת מחיר", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<PlaceDto?> OnRequestPlacePickerAsync()
    {
        var pickerVm = new PlacePickerDialogViewModel(_places);
        var window = new Window
        {
            Title = "בחר מקום",
            Owner = this,
            Width = 560,
            Height = 560,
            FlowDirection = FlowDirection.RightToLeft,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new PlacePickerDialogView { DataContext = pickerVm },
        };
        ThemeWindowChrome.ApplyThemedWindowBackground(window);
        pickerVm.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };
        await pickerVm.InitializeAsync().ConfigureAwait(true);
        return window.ShowDialog() == true ? pickerVm.SelectedPlaceDto : null;
    }

    private async Task<(CompanyDto? Company, ContactDto? Contact)> OnRequestCompanyPickerAsync()
    {
        var pickerVm = new CompanyContactPickerDialogViewModel(_companies);
        var window = new Window
        {
            Title = "בחר חברה ואיש קשר",
            Owner = this,
            Width = 720,
            Height = 560,
            FlowDirection = FlowDirection.RightToLeft,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new CompanyContactPickerDialogView { DataContext = pickerVm },
        };
        ThemeWindowChrome.ApplyThemedWindowBackground(window);
        pickerVm.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };
        await pickerVm.InitializeAsync().ConfigureAwait(true);
        return window.ShowDialog() == true
            ? (pickerVm.SelectedCompany, pickerVm.SelectedContact)
            : (null, null);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(ללא נושא)";
        var t = value.Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class AttachmentRow(EmailInboxAttachmentViewDto dto)
    {
        public int Id { get; } = dto.Id;
        public string FileName { get; } = dto.FileName;
        public string? AccItemId { get; } = dto.AccItemId;
        public bool CanOpenInAcc { get; } = dto.CanOpenInAcc;
    }
}
