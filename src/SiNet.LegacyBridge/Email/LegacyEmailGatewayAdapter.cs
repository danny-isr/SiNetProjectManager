using SiNet.Application.Abstractions.Email;

namespace SiNet.LegacyBridge.Email;

/// <summary>
/// Strangler adapter: implements the new <see cref="IEmailGateway"/> port by delegating to the
/// existing legacy <c>GoogleService</c>.
/// <para>
/// In the Foundation Round this is a stub that fails fast, so accidental early use is obvious.
/// During the Email/Google migration round it will take a dependency on the legacy service
/// (through a thin seam) and forward calls; once <c>SiNet.Infrastructure.Google</c> provides a
/// real implementation, this adapter is removed.
/// </para>
/// </summary>
public sealed class LegacyEmailGatewayAdapter : IEmailGateway
{
    private const string PendingMigration =
        "LegacyEmailGatewayAdapter is a Foundation-Round stub. Wire it to the legacy " +
        "GoogleService during the Email/Google migration round (see docs/MIGRATION_MAP.md).";

    public Task<IReadOnlyList<EmailSummary>> GetInboxAsync(int take = 50, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(PendingMigration);

    public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(PendingMigration);
}
