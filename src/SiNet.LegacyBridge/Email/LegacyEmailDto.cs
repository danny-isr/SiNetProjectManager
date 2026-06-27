namespace SiNet.LegacyBridge.Email;

/// <summary>
/// Bridge-local projection of the legacy <c>EmailInfo</c> model, carrying only the fields the
/// legacy-host email seam needs.
/// <para>
/// INACTIVE in the new stack (the active email path is the native <c>GmailEmailGateway</c> in
/// <c>SiNet.Infrastructure.Google</c>). This DTO exists so <c>SiNet.LegacyBridge</c> never
/// references the legacy <c>SiOffice.GoogleConnector</c> assembly directly: the legacy WPF layer
/// (which already references both worlds) projects <c>EmailInfo</c> into this shape when it
/// implements <see cref="ILegacyEmailSource"/>. Retained only for the legacy host.
/// </para>
/// </summary>
public sealed record LegacyEmailDto(
    string MessageId,
    string ThreadId,
    string From,
    string Subject,
    DateTimeOffset ReceivedAt,
    bool HasAttachments);
