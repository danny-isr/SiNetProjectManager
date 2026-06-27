namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Read access to the mailbox. Implemented by <c>SiNet.Infrastructure.Google</c>,
/// or temporarily by <c>SiNet.LegacyBridge</c> over the existing <c>GoogleService</c>.
/// </summary>
public interface IEmailGateway
{
    Task<IReadOnlyList<EmailSummary>> GetInboxAsync(int take = 50, CancellationToken cancellationToken = default);

    Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default);
}
