using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email;

/// <summary>
/// Persists Gmail-filed thread→project relationships into <c>ThreadStatusMapping</c> (legacy sync parity).
/// </summary>
public interface IEmailThreadMappingSyncService
{
    Task SyncFiledThreadsFromSummariesAsync(
        IReadOnlyList<EmailSummary> summaries,
        CancellationToken cancellationToken = default);
}
