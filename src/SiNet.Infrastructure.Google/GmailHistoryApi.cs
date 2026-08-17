using System.Globalization;
using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

public sealed class GmailHistoryApi(GmailClientProvider provider, IAppLogger logger) : IGmailHistoryApi
{
    private readonly GmailClientProvider _provider =
        provider ?? throw new ArgumentNullException(nameof(provider));

    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ulong?> GetProfileHistoryIdAsync(CancellationToken cancellationToken = default)
    {
        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail is null)
            return null;

        var profile = await gmail.Users.GetProfile("me")
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profile?.HistoryId is null)
            return null;

        return ulong.TryParse(
            profile.HistoryId.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;
    }

    public async Task<GmailHistoryListPage> ListHistoryPageAsync(
        ulong startHistoryId,
        string? labelId,
        IReadOnlyList<string> historyTypes,
        string? pageToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historyTypes);

        var gmail = await _provider.TryGetServiceAsync(cancellationToken).ConfigureAwait(false);
        if (gmail is null)
            throw new InvalidOperationException("Gmail service unavailable.");

        var request = gmail.Users.History.List("me");
        request.StartHistoryId = startHistoryId;
        if (!string.IsNullOrWhiteSpace(labelId))
            request.LabelId = labelId;
        if (!string.IsNullOrWhiteSpace(pageToken))
            request.PageToken = pageToken;

        foreach (var type in historyTypes)
        {
            if (string.Equals(type, "messageAdded", StringComparison.OrdinalIgnoreCase))
                request.HistoryTypes = UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded;
        }

        try
        {
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var historyIdRaw = response.HistoryId?.ToString() ?? startHistoryId.ToString(CultureInfo.InvariantCulture);
            if (!ulong.TryParse(historyIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var historyId))
                historyId = startHistoryId;

            var hasAdded = response.History is { Count: > 0 }
                && response.History.Any(h => h.MessagesAdded is { Count: > 0 });

            return new GmailHistoryListPage(historyId, hasAdded, response.NextPageToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new GmailHistoryExpiredException(
                $"Gmail history startHistoryId={startHistoryId} expired.",
                ex);
        }
    }
}
