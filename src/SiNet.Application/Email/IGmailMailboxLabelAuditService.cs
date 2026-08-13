using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email;

/// <summary>Read-only mailbox label → project table for the signed-in Gmail user (DEV-026).</summary>
public interface IGmailMailboxLabelAuditService
{
    Task<IReadOnlyList<GmailMailboxLabelAuditRow>> AuditAsync(CancellationToken cancellationToken = default);
}
