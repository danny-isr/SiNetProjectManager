using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email.QuoteSend;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Proposal <c>SendQuoteToClient</c>: open Gmail compose with tracking marker, verify Sent, or admin override.
/// </summary>
public partial class SendQuoteToClientDialog : Window, INotifyPropertyChanged
{
    public const string QuoteSentResult = "QuoteSent";
    public const string DefaultCompletionEvent = "Review.QuoteSentToClient";

    private readonly WorkSurfaceContext _context;
    private readonly ITaskCompletionService _completion;
    private readonly IEmailGateway? _emailGateway;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly ILogger? _logger;

    private readonly string _marker;
    private string _statusMessage = "פתחו Compose, שלחו, ואז בדקו שנשלח.";
    private bool _sentVerified;
    private bool _canOverride;

    public SendQuoteToClientDialog(
        WorkSurfaceContext context,
        ITaskCompletionService completion,
        IEmailGateway? emailGateway = null,
        IAuthorizationQueryService? authorization = null,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
        _emailGateway = emailGateway;
        _authorization = authorization;
        _logger = logger;

        var instanceId = context.WorkflowInstanceId is > 0
            ? context.WorkflowInstanceId.Value
            : context.TaskId is > 0 ? context.TaskId.Value : context.ProjectId;
        _marker = QuoteSendTrackingMarker.Create(Math.Max(instanceId, 1));

        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = this;
        Loaded += OnLoaded;
    }

    public string ProjectLine =>
        $"פרויקט #{_context.ProjectId} · משימה #{_context.TaskId} · מופע #{_context.WorkflowInstanceId}";

    public string MarkerLine => $"סימן מעקב: {_marker}";

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

    public bool CanCompleteVerified
    {
        get => _sentVerified;
        private set
        {
            if (_sentVerified == value) return;
            _sentVerified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCompleteVerified));
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
        if (_authorization is null)
        {
            CanOverride = false;
            return;
        }

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

    private void OpenCompose_Click(object sender, RoutedEventArgs e)
    {
        var (subject, body) = GmailComposeUrlBuilder.BuildQuoteSendContent(_marker, _context.ProjectId);
        var url = GmailComposeUrlBuilder.Build(subject, body);
        WorkflowDebugTrace.Step("SendQuote.Compose",
            $"task={_context.TaskId} marker={_marker}");

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        StatusMessage = "Compose נפתח ב-Gmail. שלחו את המייל ואז לחצו «בדוק שנשלח».";
    }

    private async void VerifySent_Click(object sender, RoutedEventArgs e)
    {
        if (_emailGateway is null)
        {
            StatusMessage = "שירות הדואר אינו זמין — לא ניתן לאמת Sent.";
            return;
        }

        StatusMessage = "בודק תיקיית Sent…";
        try
        {
            var page = await _emailGateway
                .GetMailboxPageAsync(
                    QuoteSendTrackingMarker.BuildSentSearchQuery(_marker),
                    pageToken: null,
                    CancellationToken.None)
                .ConfigureAwait(true);

            var hit = page.Items.FirstOrDefault(i =>
                QuoteSendTrackingMarker.LooksLikeMarker(i.Subject)
                || i.Snippet.Contains(_marker, StringComparison.OrdinalIgnoreCase));

            if (hit is null && page.Items.Count > 0)
            {
                // FreeText already scoped to marker; any Sent hit counts as proof.
                hit = page.Items[0];
            }

            if (hit is null)
            {
                CanCompleteVerified = false;
                StatusMessage = "לא נמצאה הוכחה ב-Sent. שלחו את המייל או בקשו override ממנהל.";
                WorkflowDebugTrace.Step("SendQuote.Verify",
                    $"task={_context.TaskId} marker={_marker} found=False");
                return;
            }

            CanCompleteVerified = true;
            var recipients = hit.To is { Value.Length: > 0 } to ? to.Value : "(לא זמין)";
            StatusMessage = $"נמצאה הוכחה ב-Sent. נמענים: {recipients}. אפשר לסיים את המשימה.";
            _logger?.LogInformation(
                "SendQuote Sent proof: task={TaskId} marker={Marker} messageId={MessageId} to={To}",
                _context.TaskId, _marker, hit.MessageId, recipients);
            WorkflowDebugTrace.Step("SendQuote.Verify",
                $"task={_context.TaskId} marker={_marker} found=True messageId={hit.MessageId}");
        }
        catch (Exception ex)
        {
            CanCompleteVerified = false;
            StatusMessage = $"שגיאה בבדיקת Sent: {ex.Message}";
            _logger?.LogWarning(ex, "SendQuote Sent verify failed for task {TaskId}", _context.TaskId);
        }
    }

    private async void CompleteVerified_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCompleteVerified)
        {
            MessageBox.Show("יש לאמת שליחה ב-Sent לפני סיום.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await CompleteAsync(overrideNote: null).ConfigureAwait(true);
    }

    private async void Override_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOverride)
        {
            MessageBox.Show("רק מנהל מערכת יכול לאשר בלי הוכחת Sent.", "שליחת הצעה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "לאשר שליחה ללא הוכחת Sent? (override מנהל)",
            "אישור מנהל",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await CompleteAsync(overrideNote: $"AdminOverride marker={_marker}").ConfigureAwait(true);
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
