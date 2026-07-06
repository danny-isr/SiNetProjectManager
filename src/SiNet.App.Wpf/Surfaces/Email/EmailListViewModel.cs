using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Read-only email list panel: rows, selection, and unread badge counts.
/// Loaded by the parent <see cref="EmailWindowViewModel"/> through <see cref="IEmailGateway"/>.
/// </summary>
public sealed class EmailListViewModel : ObservableObject
{
    private EmailListRow? _selectedEmail;

    public EmailListViewModel()
    {
        Emails = [];
    }

    public ObservableCollection<EmailListRow> Emails { get; }

    public EmailListRow? SelectedEmail
    {
        get => _selectedEmail;
        set
        {
            if (SetField(ref _selectedEmail, value))
            {
                SelectedEmailChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<EmailListRow?>? SelectedEmailChanged;

    public int UnreadEmailCount => Emails.Count(static row => row.IsUnread);

    public bool ShowUnreadCount => UnreadEmailCount > 0;

    public void ReplaceRows(IReadOnlyList<EmailListRow> rows)
    {
        Emails.Clear();
        foreach (var row in rows)
        {
            Emails.Add(row);
        }

        SelectedEmail = Emails.FirstOrDefault();
        OnPropertyChanged(nameof(UnreadEmailCount));
        OnPropertyChanged(nameof(ShowUnreadCount));
    }

    public bool TrySelectByInboxCorrelation(
        string? messageUniqueId,
        string? internetMessageId,
        string? subject,
        string? fromAddress)
    {
        EmailListRow? match = null;

        if (!string.IsNullOrWhiteSpace(messageUniqueId) || !string.IsNullOrWhiteSpace(internetMessageId))
        {
            match = Emails.FirstOrDefault(row =>
                EmailMessageIdMatcher.Matches(row.InternetMessageId, internetMessageId)
                || EmailMessageIdMatcher.Matches(row.InternetMessageId, messageUniqueId));
        }

        if (match is null && !string.IsNullOrWhiteSpace(subject))
        {
            match = Emails.FirstOrDefault(row =>
                string.Equals(row.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(fromAddress)
                    || row.Sender.Contains(fromAddress, StringComparison.OrdinalIgnoreCase)));
        }

        if (match is null)
        {
            return false;
        }

        SelectedEmail = match;
        return true;
    }
}
