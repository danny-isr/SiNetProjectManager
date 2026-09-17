using Google.Apis.Gmail.v1.Data;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

internal sealed class GmailApiLabelDirectory(GmailClientProvider provider, IAppLogger logger) : IGmailLabelDirectory
{
    private readonly GmailClientProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string SessionKey => _provider.SessionIdentity;

    public async Task<IReadOnlyList<GmailLabelRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var authenticatedBefore = _provider.IsSignedIn;
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (_provider.IsSignedIn != authenticatedBefore)
        {
            _logger.Warn(
                $"[GmailAuth] Labels.List catalog auth-changed before={authenticatedBefore} after={_provider.IsSignedIn} session={_provider.SessionIdentity}");
        }

        if (gmail is null)
            return [];

        var labels = await GmailRetry.ExecuteAsync(
            ct => gmail.Users.Labels.List("me").ExecuteAsync(ct),
            _logger,
            "Labels.List(label catalog)",
            cancellationToken).ConfigureAwait(false);

        return labels.Labels?
                   .Where(static label => !string.IsNullOrWhiteSpace(label.Id))
                   .Select(ToRecord)
                   .ToList()
               ?? [];
    }

    internal static GmailLabelRecord ToRecord(Label label) =>
        new(
            label.Id!,
            label.Name ?? string.Empty,
            label.Color?.BackgroundColor,
            label.Color?.TextColor,
            label.Type,
            label.MessagesUnread,
            label.MessagesTotal);
}
