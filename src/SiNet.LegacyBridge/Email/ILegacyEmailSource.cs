namespace SiNet.LegacyBridge.Email;

/// <summary>
/// Legacy-host seam over the legacy <c>GoogleService</c>. Mirrors only the two read
/// operations the Email/Google slice needs, expressed in terms of the bridge-local
/// <see cref="LegacyEmailDto"/> so this assembly has no dependency on
/// <c>SiOffice.GoogleConnector</c>.
/// <para>
/// INACTIVE in the new stack. The Email/Google slice has been migrated to a native
/// implementation in <c>SiNet.Infrastructure.Google</c> (<c>GmailEmailGateway</c>), which is the
/// active <see cref="SiNet.Application.Abstractions.Email.IEmailGateway"/>. This seam is NOT wired
/// into the new composition root; it is retained only because the legacy WPF host
/// (<c>SiNetProjectManagerV2</c>) still consumes it. Its concrete implementation there adapts the
/// already-authenticated <c>GoogleService</c> singleton and projects <c>EmailInfo</c> into
/// <see cref="LegacyEmailDto"/>. Remove this seam once the legacy host is retired.
/// </para>
/// </summary>
public interface ILegacyEmailSource
{
    /// <summary>
    /// Returns the emails filed under <paramref name="location"/>/<paramref name="projectName"/>.
    /// Implementations should return an empty list (not throw) when the mailbox is unavailable,
    /// e.g. the user is not signed in.
    /// </summary>
    Task<IReadOnlyList<LegacyEmailDto>> GetProjectEmailsAsync(
        string location,
        string projectName,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single email by message id, or <c>null</c> if it cannot be loaded.</summary>
    Task<LegacyEmailDto?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default);
}
