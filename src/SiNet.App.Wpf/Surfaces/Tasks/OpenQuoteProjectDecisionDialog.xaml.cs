using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Email;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Native ProjectSetup host for <c>OpenQuoteProject</c>: shows the source email and lets the
/// operator either create a project (→ <c>ProjectOpened</c>) or decline (→ <c>NotQuoteRequest</c>).
/// Filing / MoveToProject belongs to the next stage (<c>FileQuoteMaterial</c>).
/// </summary>
public partial class OpenQuoteProjectDecisionDialog : Window, INotifyPropertyChanged
{
    public const string NotQuoteRequest = "NotQuoteRequest";
    public const string ProjectOpened = "ProjectOpened";
    public const string ProjectCreatedEvent = "Review.ProjectCreated";
    public const string QuoteClassifiedEvent = "Review.QuoteRequestClassified";

    private readonly WorkSurfaceContext _context;
    private readonly ITaskCompletionService _completion;
    private readonly IProjectCreateDialogFactory _projectCreate;
    private readonly IEmailInboxQueryService? _inboxQuery;

    private string _subject = "טוען…";
    private string _fromDisplay = string.Empty;
    private string _dateDisplay = string.Empty;
    private string _statusMessage = string.Empty;

    public OpenQuoteProjectDecisionDialog(
        WorkSurfaceContext context,
        ITaskCompletionService completion,
        IProjectCreateDialogFactory projectCreate,
        IEmailInboxQueryService? inboxQuery = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
        _projectCreate = projectCreate ?? throw new ArgumentNullException(nameof(projectCreate));
        _inboxQuery = inboxQuery;

        InitializeComponent();
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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string PromptHint =>
        _context.TaskId is int taskId
            ? $"משימה #{taskId} — פתח פרויקט חדש להצעת מחיר, או סמן שזה לא רלוונטי."
            : "פתח פרויקט חדש להצעת מחיר, או סמן שזה לא רלוונטי.";

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

            Title = $"פתיחת פרויקט הצעת מחיר — {Truncate(email.Subject, 60)}";
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

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        StatusMessage = string.Empty;
        IsEnabled = false;
        try
        {
            var emailId = _context.PrimaryWorkTargetEntityId is int id && id > 0 ? id : (int?)null;
            var created = _projectCreate.ShowDialog(this, emailId);
            if (!created.Confirmed || created.ProjectId is not > 0)
            {
                IsEnabled = true;
                return;
            }

            StatusMessage = $"נוצר פרויקט #{created.ProjectId} — סוגר משימה…";
            var ok = await CompleteAsync(ProjectCreatedEvent, ProjectOpened).ConfigureAwait(true);
            if (!ok)
            {
                IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה: {ex.Message}", "פתיחת פרויקט", MessageBoxButton.OK, MessageBoxImage.Error);
            IsEnabled = true;
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
        var ok = await CompleteAsync(QuoteClassifiedEvent, NotQuoteRequest).ConfigureAwait(true);
        if (!ok)
            IsEnabled = true;
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
}
