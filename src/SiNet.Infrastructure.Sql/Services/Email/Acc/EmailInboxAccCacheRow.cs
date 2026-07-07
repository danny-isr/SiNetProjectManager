using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>Read-only DB snapshot for ACC status computation.</summary>
public sealed record EmailInboxAccCacheRow(
    int Id,
    string MessageUniqueId,
    EmailInboxStatus Status,
    string? ProcessingByLogin,
    DateTime? ProcessingStartedAtUtc,
    string? InboxAccFolderId,
    int AttachmentCount);
