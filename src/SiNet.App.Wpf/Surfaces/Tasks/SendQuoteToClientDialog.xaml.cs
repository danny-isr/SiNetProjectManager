using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.QuoteSend;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Proposal <c>SendQuoteToClient</c>: internal SiNet compose + explicit Send via
/// <see cref="IQuoteSendComposeService"/> (narrow G-Policy exception).
/// </summary>
public partial class SendQuoteToClientDialog : Window, INotifyPropertyChanged
{
    public const string QuoteSentResult = "QuoteSent";
    public const string DefaultCompletionEvent = "Review.QuoteSentToClient";

    private readonly WorkSurfaceContext _context;
    private readonly ITaskCompletionService _completion;
    private readonly IQuoteSendComposeService? _compose;
    private readonly IQuoteSendAttachmentService? _attachmentService;
    private readonly IEmailGateway? _emailGateway;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly ILogger? _logger;

    private QuoteSendComposeDraft? _draft;
    private readonly List<EmailAttachment> _attachments = [];
    private string _statusMessage = "טוען טיוטה…";
    private string _toText = string.Empty;
    private string _ccText = string.Empty;
    private string _subjectText = string.Empty;
    private string _bodyText = string.Empty;
    private bool _sentVerified;
    private bool _canOverride;
    private bool _isBusy;

    public SendQuoteToClientDialog(
        WorkSurfaceContext context,
        ITaskCompletionService completion,
        IQuoteSendComposeService? compose = null,
        IQuoteSendAttachmentService? attachmentService = null,
        IEmailGateway? emailGateway = null,
        IAuthorizationQueryService? authorization = null,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
        _compose = compose;
        _attachmentService = attachmentService;
        _emailGateway = emailGateway;
        _authorization = authorization;
        _logger = logger;

        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = this;
        Loaded += OnLoaded;
    }

    public string ProjectLine =>
        $"פרויקט #{_context.ProjectId} · משימה #{_context.TaskId} · מופע #{_context.WorkflowInstanceId}";

    public string ModeLine =>
        _draft is null
            ? string.Empty
            : _draft.Mode == QuoteSendComposeMode.ReplyAll
                ? $"מצב: Reply-All לשרשור (מקור inbox #{_draft.SourceInboxMessageId?.ToString() ?? "?"})"
                : "מצב: Compose חדש (לא Reply)";

    public string AttachmentLine =>
        _attachments.Count == 0
            ? "ללא קבצים מצורפים"
            : $"מצורפים: {string.Join(", ", _attachments.Select(a => a.FileName))}";

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string ToText
    {
        get => _toText;
        set { if (_toText == value) return; _toText = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSend)); }
    }

    public string CcText
    {
        get => _ccText;
        set { if (_ccText == value) return; _ccText = value; OnPropertyChanged(); }
    }

    public string SubjectText
    {
        get => _subjectText;
        set { if (_subjectText == value) return; _subjectText = value; OnPropertyChanged(); }
    }

    public string BodyText
    {
        get => _bodyText;
        set { if (_bodyText == value) return; _bodyText = value; OnPropertyChanged(); }
    }

    public bool CanSend => !_isBusy && _compose is not null && !string.IsNullOrWhiteSpace(ToText);

    public bool CanCompleteVerified
    {
        get => _sentVerified;
        private set
        {
            if (_sentVerified == value) return;
            _sentVerified = value;
            OnPropertyChanged();
        }
    }

    public bool CanOverride
    {
        get => _canOverride;
        private set
        {
            if (_canOverride == value) return;
            _canOverride = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_authorization is not null)
        {
            try
            {
                CanOverride = await _authorization
                    .IsCurrentUserInRoleAsync(AppRole.Administrator, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SendQuote: failed to resolve Administrator override eligibility.");
                CanOverride = false;
            }
        }

        if (_compose is null)
        {
            StatusMessage = "שירות Compose אינו זמין.";
            return;
        }

        if (_context.TaskId is int taskId && taskId > 0)
        {
            var existing = await _compose.GetProofAsync(taskId, CancellationToken.None).ConfigureAwait(true);
            if (existing is not null)
            {
                CanCompleteVerified = true;
                StatusMessage = $"נמצאה הוכחת שליחה קיימת (MessageId={existing.GmailMessageId}). אפשר לסיים.";
            }
        }

        var source = await _compose
            .GetProposalSourceEmailAsync(_context.WorkflowInstanceId, CancellationToken.None)
            .ConfigureAwait(true);

        if (source is null && !CanCompleteVerified)
        {
            var choice = MessageBox.Show(
                "לא נמצא מייל מקור מקושר לתהליך ההצעה.\n\nכן = בחירת מייל מקור (Reply-All)\nלא = המשך כ־Compose חדש",
                "מייל מקור",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (choice == MessageBoxResult.Yes)
            {
                await PickAndApplySourceAsync().ConfigureAwait(true);
                if (_draft is null)
                    await LoadDraftAsync(QuoteSendComposeMode.NewCompose, preferredInbox: null).ConfigureAwait(true);
                return;
            }

            await LoadDraftAsync(QuoteSendComposeMode.NewCompose, preferredInbox: null).ConfigureAwait(true);
            return;
        }

        await LoadDraftAsync(QuoteSendComposeMode.ReplyAll, preferredInbox: source?.InboxMessageId)
            .ConfigureAwait(true);
    }

    private async Task LoadDraftAsync(QuoteSendComposeMode mode, int? preferredInbox)
    {
        if (_compose is null)
            return;

        SetBusy(true);
        try
        {
            _draft = await _compose.CreateDraftAsync(
                    _context.ProjectId,
                    _context.WorkflowInstanceId,
                    preferredInbox,
                    mode,
                    CancellationToken.None)
                .ConfigureAwait(true);

            ApplyDraftToUi(_draft);
            StatusMessage = mode == QuoteSendComposeMode.ReplyAll && preferredInbox is > 0
                ? "טיוטת Reply-All מוכנה. ערוך במידת הצורך ולחץ «שלח»."
                : "טיוטת Compose חדש מוכנה. מלא נמענים ולחץ «שלח».";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SendQuote draft load failed.");
            StatusMessage = $"טעינת טיוטה נכשלה: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyDraftToUi(QuoteSendComposeDraft draft)
    {
        ToText = string.Join("; ", draft.To);
        CcText = string.Join("; ", draft.Cc);
        SubjectText = draft.Subject;
        BodyText = draft.Body;
        OnPropertyChanged(nameof(ModeLine));
    }

    private QuoteSendComposeDraft? CaptureDraftFromUi()
    {
        if (_draft is null)
            return null;

        return _draft with
        {
            To = SplitAddresses(ToText),
            Cc = SplitAddresses(CcText),
            Subject = SubjectText?.Trim() ?? string.Empty,
            Body = BodyText ?? string.Empty,
        };
    }

    private static IReadOnlyList<string> SplitAddresses(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Contains('@', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_compose is null || _context.TaskId is not int taskId || taskId <= 0)
        {
            MessageBox.Show("חסר שירות שליחה או מזהה משימה.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var userId = _context.ActingUserId ?? 0;
        if (userId <= 0)
        {
            MessageBox.Show("חסר משתמש מחובר לשליחה.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var draft = CaptureDraftFromUi();
        if (draft is null || draft.To.Count == 0)
        {
            MessageBox.Show("יש למלא לפחות נמען אחד ב־אל.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SubjectText.Contains(QuoteSendTrackingMarker.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("אין לכלול את סימן המעקב בכותרת.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        StatusMessage = "שולח…";
        try
        {
            WorkflowDebugTrace.Step("SendQuote.Compose",
                $"task={taskId} marker={draft.Marker} mode={draft.Mode} to={draft.To.Count} cc={draft.Cc.Count}");

            var result = await _compose
                .SendAsync(taskId, userId, draft, _attachments, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                CanCompleteVerified = false;
                StatusMessage = result.RequiresConsent
                    ? $"נדרש אישור Google לשליחה: {result.Error}"
                    : $"השליחה נכשלה: {result.Error}";
                WorkflowDebugTrace.Step("SendQuote.Verify",
                    $"task={taskId} marker={draft.Marker} found=False error={result.Error}");
                return;
            }

            CanCompleteVerified = true;
            StatusMessage = $"נשלח בהצלחה. MessageId={result.MessageId}. אפשר לסיים את המשימה.";
            _logger?.LogInformation(
                "SendQuote sent: task={TaskId} marker={Marker} messageId={MessageId}",
                taskId, draft.Marker, result.MessageId);
            WorkflowDebugTrace.Step("SendQuote.Verify",
                $"task={taskId} marker={draft.Marker} found=True messageId={result.MessageId}");
        }
        catch (Exception ex)
        {
            CanCompleteVerified = false;
            StatusMessage = $"שגיאה בשליחה: {ex.Message}";
            _logger?.LogWarning(ex, "SendQuote send failed for task {TaskId}", taskId);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PickSource_Click(object sender, RoutedEventArgs e)
        => await PickAndApplySourceAsync().ConfigureAwait(true);

    private async Task PickAndApplySourceAsync()
    {
        if (_emailGateway is null || _compose is null)
        {
            MessageBox.Show("שירות הדואר אינו זמין לבחירת מייל מקור.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        StatusMessage = "טוען מיילים לבחירה…";
        try
        {
            var page = await _emailGateway.GetMailboxPageAsync(
                new EmailMailboxQuery
                {
                    MailboxScope = EmailMailboxScope.AllMail,
                    PageSize = 40,
                },
                pageToken: null,
                CancellationToken.None).ConfigureAwait(true);

            if (page.Items.Count == 0)
            {
                StatusMessage = "לא נמצאו מיילים לבחירה.";
                return;
            }

            var picker = new QuoteSourceEmailPickerDialog(page.Items) { Owner = this };
            if (picker.ShowDialog() != true || picker.Selected is null)
            {
                StatusMessage = "בחירת מייל מקור בוטלה.";
                return;
            }

            // Resolve via rfc822 when possible; otherwise build Reply-All from summary+details.
            var details = await _emailGateway
                .GetDetailsAsync(picker.Selected.MessageId, CancellationToken.None)
                .ConfigureAwait(true);
            if (details is null)
            {
                StatusMessage = "לא ניתן לטעון את פרטי המייל שנבחר.";
                return;
            }

            var instanceId = _context.WorkflowInstanceId is > 0
                ? _context.WorkflowInstanceId.Value
                : Math.Max(_context.ProjectId, 1);
            var marker = QuoteSendTrackingMarker.Create(instanceId);
            _draft = QuoteReplyAllComposer.BuildReplyAll(
                details,
                currentUserEmail: null,
                _context.ProjectId,
                marker);
            ApplyDraftToUi(_draft);
            StatusMessage = "טיוטת Reply-All לפי המייל שנבחר. לחץ «שלח».";
        }
        catch (Exception ex)
        {
            StatusMessage = $"בחירת מייל נכשלה: {ex.Message}";
            _logger?.LogWarning(ex, "SendQuote pick source failed.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewCompose_Click(object sender, RoutedEventArgs e)
        => await LoadDraftAsync(QuoteSendComposeMode.NewCompose, preferredInbox: null).ConfigureAwait(true);

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = false,
            CheckFileExists = true,
            Filter = "PDF (*.pdf)|*.pdf",
            Title = "בחר הצעת מחיר לשליחה (PDF)",
        };

        if (_attachmentService is not null)
        {
            try
            {
                var initial = await _attachmentService
                    .ResolveAttachInitialDirectoryAsync(_context.ProjectId, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                    dialog.InitialDirectory = initial;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SendQuote: failed to resolve ניהול_כספי initial directory.");
            }
        }

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
            return;

        var path = dialog.FileName;
        try
        {
            if (_attachmentService is not null)
            {
                var filed = await _attachmentService
                    .EnsureFiledIfNeededAsync(_context.ProjectId, path, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!filed.Success)
                {
                    MessageBox.Show(
                        $"הצירוף בוצע, אך התיוק לקטלוג נכשל: {filed.Error}",
                        "שליחת הצעה",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else if (filed.FiledNow)
                {
                    StatusMessage = "הקובץ תויק כ«הצעת_מחיר_לשליחה».";
                    WorkflowDebugTrace.Step("SendQuote.File",
                        $"project={_context.ProjectId} filed=True path={filed.FiledCanonicalPath}");
                }
                else if (filed.AlreadyFiled)
                {
                    StatusMessage = "הקובץ כבר מתויק כ«הצעת_מחיר_לשליחה» — לא תויק מחדש.";
                    WorkflowDebugTrace.Step("SendQuote.File",
                        $"project={_context.ProjectId} alreadyFiled=True path={filed.FiledCanonicalPath}");
                }
            }

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            _attachments.Clear();
            _attachments.Add(new EmailAttachment(
                Path.GetFileName(path),
                "application/pdf",
                bytes));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"לא ניתן לצרף '{path}': {ex.Message}", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        OnPropertyChanged(nameof(AttachmentLine));
    }

    private async void CompleteVerified_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCompleteVerified)
        {
            MessageBox.Show("יש לשלוח בהצלחה לפני סיום (או לבקש override).", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await CompleteAsync(overrideNote: null).ConfigureAwait(true);
    }

    private async void Override_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOverride)
        {
            MessageBox.Show("רק מנהל מערכת יכול לאשר בלי הוכחת שליחה.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "לאשר שליחה ללא הוכחת MessageId? (override מנהל)",
            "אישור מנהל",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await CompleteAsync(overrideNote: $"AdminOverride marker={_draft?.Marker}").ConfigureAwait(true);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task CompleteAsync(string? overrideNote)
    {
        if (_context.TaskId is not int taskId || taskId <= 0)
        {
            MessageBox.Show("חסר מזהה משימה.", "שליחת הצעה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var userId = _context.ActingUserId ?? 0;
        if (userId <= 0)
        {
            MessageBox.Show("חסר משתמש מחובר להשלמת המשימה.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var eventCode = string.IsNullOrWhiteSpace(_context.CompletionEventCode)
            ? DefaultCompletionEvent
            : _context.CompletionEventCode!;

        if (!string.IsNullOrWhiteSpace(overrideNote))
        {
            _logger?.LogWarning(
                "SendQuote admin override: task={TaskId} user={UserId} note={Note}",
                taskId, userId, overrideNote);
            WorkflowDebugTrace.Step("SendQuote.Override",
                $"task={taskId} user={userId} {overrideNote}");
        }

        try
        {
            var result = await _completion.CompleteAsync(
                new CompleteTaskCommand(
                    taskId,
                    eventCode,
                    QuoteSentResult,
                    CompletedTaskLinkIds: null,
                    userId),
                CancellationToken.None).ConfigureAwait(true);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "השלמת המשימה נכשלה.",
                    "שליחת הצעה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendQuote complete failed for task {TaskId}", taskId);
            MessageBox.Show(ex.Message, "שליחת הצעה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OnPropertyChanged(nameof(CanSend));
        IsEnabled = !busy;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
