using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

public sealed partial class EmailListViewModel
{
    private sealed class DesignEmailListGateway : IEmailGateway
    {
        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(string location, string projectName, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(string projectLabelName, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(EmailMailboxQuery query, string? pageToken = null, CancellationToken cancellationToken = default)
        {
            var items = EmailWindowDesignData.SampleEmails
                .Select(static row => new EmailSummary(
                    row.Id,
                    $"thread-{row.Id}",
                    EmailAddress.CreateOrFallback(row.Sender),
                    row.Subject,
                    row.ReceivedOn == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(row.ReceivedOn),
                    row.AttachmentCount,
                    InternetMessageId: null,
                    To: null,
                    Snippet: row.Preview,
                    LabelNames: [row.GroupName],
                    PrimaryLabel: row.GroupName,
                    IsUnread: row.IsUnread))
                .ToList();

            return Task.FromResult(new EmailMailboxPage(items, query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
                new GmailLabelInfo("lbl1", "פרויקטים_משרד/תל אביב/1042 — דוגמה"),
            ]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(2, IsExact: true, EmailMailboxQueryComposer.DescribeMailboxScope(query)));
    }

    private sealed class DesignAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; } = true;

        public string? ConnectedAccountEmail { get; private set; } = "design@example.com";

        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
        {
            IsAuthenticated = true;
            ConnectedAccountEmail = "design@example.com";
            AuthStateChanged?.Invoke(true);
            return Task.FromResult(true);
        }

        public void Logout()
        {
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            Logout();
            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsAuthenticated);

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
