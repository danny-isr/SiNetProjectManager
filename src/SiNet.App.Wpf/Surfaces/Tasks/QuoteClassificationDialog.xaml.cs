using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Email;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Native classification host for <c>IdentifyQuoteRequest</c>: shows source email subject/from/date
/// and lets the operator pick QuoteRequestDetected / NotQuoteRequest, then completes via
/// <see cref="ITaskCompletionService"/>.
/// </summary>
public partial class QuoteClassificationDialog : Window, INotifyPropertyChanged
{
    public const string QuoteRequestDetected = "QuoteRequestDetected";
    public const string NotQuoteRequest = "NotQuoteRequest";
    public const string DefaultCompletionEvent = "Review.QuoteRequestClassified";

    private readonly WorkSurfaceContext _context;
    private readonly ITaskCompletionService _completion;
    private readonly IEmailInboxQueryService? _inboxQuery;

    private string _subject = "טוען…";
    private string _fromDisplay = string.Empty;
    private string _dateDisplay = string.Empty;

    public QuoteClassificationDialog(
        WorkSurfaceContext context,
        ITaskCompletionService completion,
        IEmailInboxQueryService? inboxQuery = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
        _inboxQuery = inboxQuery;

        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = this;
        Loaded += OnLoaded;
    }

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

    public string PromptHint =>
        _context.TaskId is int taskId
            ? $"משימה #{taskId} — בחר תוצאה כדי להמשיך את תהליך הצעת המחיר."
            : "בחר תוצאה כדי להמשיך את תהליך הצעת המחיר.";

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await LoadEmailAsync().ConfigureAwait(true);
    }

    private async Task LoadEmailAsync()
    {
        if (_context.PrimaryWorkTargetEntityId is not int emailId || emailId <= 0 || _inboxQuery is null)
        {
            Subject = _context.PrimaryWorkTargetEntityId is int id
                ? $"מייל מקור #{id}"
                : "(אין קישור למייל מקור)";
            return;
        }

        try
        {
            var email = await _inboxQuery.GetByIdAsync(emailId).ConfigureAwait(true);
            if (email is null)
            {
                Subject = $"מייל #{emailId} לא נמצא";
                return;
            }

            Title = $"סיווג בקשת הצעת מחיר — מייל #{email.Id}";
            Subject = string.IsNullOrWhiteSpace(email.Subject) ? "(ללא נושא)" : email.Subject!;
            FromDisplay = string.IsNullOrWhiteSpace(email.FromAddress)
                ? "מאת: (לא ידוע)"
                : $"מאת: {email.FromAddress}";
            DateDisplay = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        }
        catch (Exception ex)
        {
            Subject = $"שגיאה בטעינת המייל: {ex.Message}";
        }
    }

    private async void QuoteRequest_Click(object sender, RoutedEventArgs e) =>
        await CompleteAsync(QuoteRequestDetected).ConfigureAwait(true);

    private async void NotQuote_Click(object sender, RoutedEventArgs e) =>
        await CompleteAsync(NotQuoteRequest).ConfigureAwait(true);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task CompleteAsync(string resultCode)
    {
        if (_context.TaskId is not int taskId || taskId <= 0)
        {
            MessageBox.Show("חסר מזהה משימה.", "סיווג", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var userId = _context.ActingUserId ?? 0;
        if (userId <= 0)
        {
            MessageBox.Show("חסר משתמש מחובר להשלמת המשימה.", "סיווג", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var eventCode = string.IsNullOrWhiteSpace(_context.CompletionEventCode)
            ? DefaultCompletionEvent
            : _context.CompletionEventCode!;

        IsEnabled = false;
        try
        {
            var result = await _completion.CompleteAsync(
                new CompleteTaskCommand(
                    taskId,
                    eventCode,
                    resultCode,
                    CompletedTaskLinkIds: null,
                    userId),
                CancellationToken.None).ConfigureAwait(true);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "השלמת המשימה נכשלה.",
                    "סיווג",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                IsEnabled = true;
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה: {ex.Message}", "סיווג", MessageBoxButton.OK, MessageBoxImage.Error);
            IsEnabled = true;
        }
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
