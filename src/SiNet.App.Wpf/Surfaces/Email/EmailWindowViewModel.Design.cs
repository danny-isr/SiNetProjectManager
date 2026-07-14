using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

public sealed partial class EmailWindowViewModel
{
    private sealed class DesignEmailGateway : IEmailGateway
    {
        private static readonly IReadOnlyList<EmailSummary> SampleEmails = EmailWindowDesignData.SampleEmails
            .Select(static row => new EmailSummary(
                row.Id,
                $"thread-{row.Id}",
                EmailAddress.CreateOrFallback(row.Sender),
                row.Subject,
                row.ReceivedOn == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(row.ReceivedOn),
                row.AttachmentCount))
            .ToList();

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleEmails.FirstOrDefault(email => string.Equals(email.MessageId, messageId, StringComparison.Ordinal)));

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default)
        {
            var summary = SampleEmails.FirstOrDefault(email => string.Equals(email.MessageId, messageId, StringComparison.Ordinal));
            if (summary is null)
            {
                return Task.FromResult<EmailMessageDetails?>(null);
            }

            return Task.FromResult<EmailMessageDetails?>(new EmailMessageDetails(
                summary.MessageId,
                summary.ThreadId,
                summary.From,
                summary.Subject,
                summary.ReceivedAt,
                EmailWindowDesignData.SampleBody,
                EmailWindowDesignData.SampleAttachments
                    .Select((attachment, index) => new EmailMessageAttachmentDetails(
                        $"att-{index + 1}",
                        attachment.FileName,
                        attachment.Kind,
                        null))
                    .ToList()));
        }

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
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
                    PrimaryLabel: row.PrimaryLabel ?? row.GroupName))
                .ToList();

            return Task.FromResult(new EmailMailboxPage(items, query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GmailLabelInfo>>([
                new GmailLabelInfo("INBOX", "INBOX"),
            ]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));
    }

    private sealed class DesignConnectorAuthService : IConnectorAuthService
    {
        public bool IsAuthenticated { get; private set; }

        public string? ConnectedAccountEmail { get; private set; }

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
