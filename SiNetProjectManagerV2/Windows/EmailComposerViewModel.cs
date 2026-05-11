using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SiNetSQL.DTOs.Email;
using SiNetSQL.MVVM;
using SiNetSQL.Services.EmailOutbound;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Windows;

public sealed class EmailComposerViewModel : INotifyPropertyChanged
{
    private readonly IOutboundMailService _mailService;
    private readonly EmailComposerContext _context;
    private string? _fromAddress;
    private string _to = string.Empty;
    private string _cc = string.Empty;
    private string _bcc = string.Empty;
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private RecipientField _activeRecipientField = RecipientField.To;
    private int _activeRecipientCaretIndex;
    private EmailRecipientSuggestion? _selectedRecipientSuggestion;
    private bool _isRecipientSuggestionsOpen;
    private bool _isSending;
    private string? _statusMessage;

    public EmailComposerViewModel(EmailComposerContext context, IOutboundMailService mailService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));

        FromAddress = context.FromAddress;
        AvailableFromAddresses = new ObservableCollection<string>(context.AvailableFromAddresses);
        To = string.Join("; ", context.To);
        Cc = string.Join("; ", context.Cc);
        Bcc = string.Join("; ", context.Bcc);
        Subject = context.Subject;
        Body = context.Body;
        Attachments = new ObservableCollection<EmailAttachmentInfo>(context.Attachments);
        RecipientSuggestions = new ObservableCollection<EmailRecipientSuggestion>(context.RecipientSuggestions);
        FilteredRecipientSuggestions = new ObservableCollection<EmailRecipientSuggestion>();
        StatusMessage = context.UserMessage;

        AddAttachmentCommand = new RelayCommand<object>(_ => AddAttachments(), _ => !IsSending);
        RemoveAttachmentCommand = new RelayCommand<EmailAttachmentInfo>(RemoveAttachment, a => !IsSending && a != null);
        AddRecipientSuggestionCommand = new RelayCommand<EmailRecipientSuggestion>(AddRecipientSuggestion, s => !IsSending && s != null);
        SendCommand = new RelayCommand<object>(_ => _ = SendAsync(), _ => !IsSending);
        CancelCommand = new RelayCommand<object>(_ => RequestClose?.Invoke(false), _ => !IsSending);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool?>? RequestClose;
    public EmailSendResult? SendResult { get; private set; }

    public ObservableCollection<string> AvailableFromAddresses { get; }
    public ObservableCollection<EmailRecipientSuggestion> RecipientSuggestions { get; }
    public ObservableCollection<EmailRecipientSuggestion> FilteredRecipientSuggestions { get; }
    public ObservableCollection<EmailAttachmentInfo> Attachments { get; }

    public void AddRecipientSuggestions(IEnumerable<EmailRecipientSuggestion> suggestions)
    {
        foreach (var suggestion in suggestions)
        {
            if (string.IsNullOrWhiteSpace(suggestion.Email)
                || RecipientSuggestions.Any(s => string.Equals(s.Email, suggestion.Email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            RecipientSuggestions.Add(suggestion);
        }
    }

    public string? FromAddress
    {
        get => _fromAddress;
        set { _fromAddress = value; OnPropertyChanged(); }
    }

    public string To
    {
        get => _to;
        set { _to = value; OnPropertyChanged(); }
    }

    public string Cc
    {
        get => _cc;
        set { _cc = value; OnPropertyChanged(); }
    }

    public string Bcc
    {
        get => _bcc;
        set { _bcc = value; OnPropertyChanged(); }
    }

    public string Subject
    {
        get => _subject;
        set { _subject = value; OnPropertyChanged(); }
    }

    public string Body
    {
        get => _body;
        set { _body = value; OnPropertyChanged(); }
    }

    public EmailRecipientSuggestion? SelectedRecipientSuggestion
    {
        get => _selectedRecipientSuggestion;
        set { _selectedRecipientSuggestion = value; OnPropertyChanged(); }
    }

    public bool IsRecipientSuggestionsOpen
    {
        get => _isRecipientSuggestionsOpen;
        private set
        {
            _isRecipientSuggestionsOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToRecipientSuggestionsOpen));
            OnPropertyChanged(nameof(IsCcRecipientSuggestionsOpen));
            OnPropertyChanged(nameof(IsBccRecipientSuggestionsOpen));
        }
    }

    public bool IsToRecipientSuggestionsOpen => IsRecipientSuggestionsOpen && _activeRecipientField == RecipientField.To;

    public bool IsCcRecipientSuggestionsOpen => IsRecipientSuggestionsOpen && _activeRecipientField == RecipientField.Cc;

    public bool IsBccRecipientSuggestionsOpen => IsRecipientSuggestionsOpen && _activeRecipientField == RecipientField.Bcc;

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (_isSending == value) return;
            _isSending = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand AddAttachmentCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand AddRecipientSuggestionCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(To))
        {
            MessageBox.Show("יש להזין לפחות נמען אחד.", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(Subject))
        {
            MessageBox.Show("יש להזין נושא למייל.", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            MessageBox.Show("יש להזין תוכן למייל.", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var attachment in Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
            {
                MessageBox.Show($"הקובץ המצורף לא נמצא:\n{attachment.FileName}", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var invalidAddresses = SplitAddresses(To)
            .Concat(SplitAddresses(Cc))
            .Concat(SplitAddresses(Bcc))
            .Where(a => !IsValidEmailAddress(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidAddresses.Count > 0)
        {
            MessageBox.Show(
                $"נמצאו כתובות מייל לא תקינות:\n{string.Join("\n", invalidAddresses)}",
                "שליחת מייל",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsSending = true;
        StatusMessage = "שולח מייל דרך Gmail...";

        try
        {
            var request = new EmailSendRequest
            {
                FromAddress = FromAddress,
                To = SplitAddresses(To),
                Cc = SplitAddresses(Cc),
                Bcc = SplitAddresses(Bcc),
                Subject = Subject,
                BodyText = Body,
                Attachments = Attachments.ToList(),
                RelatedEntityType = _context.EntityType,
                RelatedEntityId = _context.EntityId
            };

            SendResult = await _mailService.SendAsync(request, CancellationToken.None);
            if (SendResult.Success)
            {
                StatusMessage = "המייל נשלח בהצלחה.";
                MessageBox.Show("המייל נשלח בהצלחה.", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke(true);
            }
            else
            {
                StatusMessage = SendResult.ErrorMessage;
                MessageBox.Show(SendResult.ErrorMessage ?? "שליחת המייל נכשלה.", "שליחת מייל", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    private void AddAttachments()
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחר קבצים לצירוף",
            Multiselect = true,
            Filter = "קבצים נפוצים|*.pdf;*.dwf;*.dwfx;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|כל הקבצים|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        foreach (var fileName in dialog.FileNames)
        {
            var file = new FileInfo(fileName);
            if (!file.Exists) continue;

            Attachments.Add(new EmailAttachmentInfo
            {
                FileName = file.Name,
                LocalPath = file.FullName,
                ContentType = GetContentType(file.Extension),
                SizeBytes = file.Length,
                IsTemporary = false,
                SourceDescription = "בחירת משתמש"
            });
        }
    }

    private void RemoveAttachment(EmailAttachmentInfo? attachment)
    {
        if (attachment != null)
            Attachments.Remove(attachment);
    }

    public void RefreshRecipientSuggestions(RecipientField field, string text, int caretIndex)
    {
        _activeRecipientField = field;
        _activeRecipientCaretIndex = caretIndex;
        OnPropertyChanged(nameof(IsToRecipientSuggestionsOpen));
        OnPropertyChanged(nameof(IsCcRecipientSuggestionsOpen));
        OnPropertyChanged(nameof(IsBccRecipientSuggestionsOpen));
        FilteredRecipientSuggestions.Clear();
        SelectedRecipientSuggestion = null;

        try
        {
            var query = GetCurrentRecipientQuery(text, caretIndex);
            if (query.Length < 2)
            {
                IsRecipientSuggestionsOpen = false;
                return;
            }

            var currentRecipients = SplitAddresses(To)
                .Concat(SplitAddresses(Cc))
                .Concat(SplitAddresses(Bcc))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var results = RecipientSuggestions
                .Where(s => !currentRecipients.Contains(s.Email))
                .Where(s => MatchesSuggestion(s, query))
                .GroupBy(s => s.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(10)
                .ToList();

            foreach (var suggestion in results)
                FilteredRecipientSuggestions.Add(suggestion);

            IsRecipientSuggestionsOpen = FilteredRecipientSuggestions.Count > 0;

            ReportLogger.Info(
                $"Operation=EmailRecipientAutocomplete Field={field} QueryLength={query.Length} ResultsCount={FilteredRecipientSuggestions.Count} Source=DbContacts Result={(IsRecipientSuggestionsOpen ? "Shown" : "NoResults")}");
        }
        catch (Exception ex)
        {
            IsRecipientSuggestionsOpen = false;
            ReportLogger.Warn(
                $"Operation=EmailRecipientAutocomplete Field={field} QueryLength=0 ResultsCount=0 Source=DbContacts Result=Error Reason={ex.Message}");
        }
    }

    public void CloseRecipientSuggestions()
    {
        IsRecipientSuggestionsOpen = false;
        SelectedRecipientSuggestion = null;
    }

    private void AddRecipientSuggestion(EmailRecipientSuggestion? suggestion)
    {
        if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.Email))
            return;

        var email = suggestion.Email.Trim();
        var replacement = _activeRecipientField switch
        {
            RecipientField.Cc => ReplaceCurrentRecipientPart(Cc, email, _activeRecipientCaretIndex),
            RecipientField.Bcc => ReplaceCurrentRecipientPart(Bcc, email, _activeRecipientCaretIndex),
            _ => ReplaceCurrentRecipientPart(To, email, _activeRecipientCaretIndex)
        };

        switch (_activeRecipientField)
        {
            case RecipientField.Cc:
                Cc = replacement;
                break;
            case RecipientField.Bcc:
                Bcc = replacement;
                break;
            default:
                To = replacement;
                break;
        }

        IsRecipientSuggestionsOpen = false;
        SelectedRecipientSuggestion = null;
    }

    private static bool MatchesSuggestion(EmailRecipientSuggestion suggestion, string query)
    {
        return suggestion.Email.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (!string.IsNullOrWhiteSpace(suggestion.DisplayName)
                && suggestion.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string GetCurrentRecipientQuery(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var safeCaretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var lastSeparator = text.LastIndexOfAny([';', ','], Math.Max(0, safeCaretIndex - 1));
        var start = lastSeparator < 0 ? 0 : lastSeparator + 1;
        return text[start..safeCaretIndex].Trim();
    }

    private static string ReplaceCurrentRecipientPart(string text, string email, int caretIndex)
    {
        var safeCaretIndex = Math.Clamp(caretIndex, 0, text.Length);
        var startSeparator = text.LastIndexOfAny([';', ','], Math.Max(0, safeCaretIndex - 1));
        var endSeparator = text.IndexOfAny([';', ','], safeCaretIndex);
        var start = startSeparator < 0 ? 0 : startSeparator + 1;
        var end = endSeparator < 0 ? text.Length : endSeparator;
        var prefix = text[..start].TrimEnd();
        var suffix = endSeparator < 0 ? string.Empty : text[end..];
        var separator = string.IsNullOrWhiteSpace(prefix) ? string.Empty : " ";
        return $"{prefix}{separator}{email}{suffix}";
    }

    private static List<string> SplitAddresses(string value) => value
        .Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .ToList();

    private static bool IsValidEmailAddress(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        ".bmp" => "image/bmp",
        ".dwf" => "model/vnd.dwf",
        ".dwfx" => "model/vnd.dwfx+xps",
        _ => "application/octet-stream"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
