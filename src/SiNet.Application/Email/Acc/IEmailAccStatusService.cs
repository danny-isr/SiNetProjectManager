namespace SiNet.Application.Email.Acc;

/// <summary>
/// Targeted ACC inbox status for the Email Workbench. Read-only — does not upload or mutate ACC.
/// </summary>
public interface IEmailAccStatusService
{
    Task<EmailAccInboxStatus?> GetStatusByInternetMessageIdAsync(
        string? internetMessageId,
        string gmailMessageId,
        string? currentUserLogin = null,
        CancellationToken cancellationToken = default);

    Task<EmailAccInboxStatus?> GetStatusByInboxMessageIdAsync(
        int inboxMessageId,
        string? currentUserLogin = null,
        CancellationToken cancellationToken = default);
}
