using SiNet.Domain.ValueObjects;

namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Lightweight, UI-agnostic summary of an email message. Contains no WPF types
/// (colors/brushes belong to the WPF layer, not the connector).
/// </summary>
public sealed record EmailSummary(
    string MessageId,
    string ThreadId,
    EmailAddress From,
    string Subject,
    DateTimeOffset ReceivedAt,
    bool HasAttachments);
