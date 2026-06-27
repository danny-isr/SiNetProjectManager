namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Orchestrates mailbox synchronization. Returns the number of messages processed.
/// </summary>
public interface IEmailSyncService
{
    Task<int> SyncAsync(CancellationToken cancellationToken = default);
}
