namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Send access to the mailbox. Implemented by <c>SiNet.Infrastructure.Google</c> over the native
/// Gmail API. The read counterpart is <see cref="IEmailGateway"/>.
/// <para>
/// Implementations are non-throwing for expected failures: when the mailbox is unavailable
/// (not signed in) or the session lacks the send scope, they return a failed
/// <see cref="EmailSendResult"/> (with <see cref="EmailSendResult.RequiresConsent"/> set where
/// applicable) rather than throwing.
/// </para>
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email described by <paramref name="request"/>. Returns an
    /// <see cref="EmailSendResult"/> describing success (with the sent message id) or the failure
    /// reason. Does not throw for expected error paths.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken = default);
}
