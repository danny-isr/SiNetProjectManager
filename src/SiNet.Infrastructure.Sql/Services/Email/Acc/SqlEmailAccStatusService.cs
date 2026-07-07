using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

internal sealed class SqlEmailAccStatusService(
    EmailAccInboxQueryService inboxQuery,
    IAccInboxReconciliationService? reconciliationService = null)
    : IEmailAccStatusService
{
    private readonly EmailAccInboxQueryService _inboxQuery =
        inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));

    private readonly IAccInboxReconciliationService? _reconciliationService = reconciliationService;

    public async Task<EmailAccInboxStatus?> GetStatusByInternetMessageIdAsync(
        string? internetMessageId,
        string gmailMessageId,
        string? currentUserLogin = null,
        CancellationToken cancellationToken = default)
    {
        var messageUniqueId = EmailAccStatusMapper.ResolveMessageUniqueId(internetMessageId, gmailMessageId);
        var cache = await _inboxQuery.GetByMessageUniqueIdAsync(messageUniqueId, cancellationToken)
            .ConfigureAwait(false);

        AccInboxReconciliationResult? reconciliation = null;
        if (_reconciliationService is not null)
        {
            reconciliation = cache?.Id is int id and > 0
                ? await _reconciliationService.ReconcileByMessageIdAsync(id, cancellationToken).ConfigureAwait(false)
                : await _reconciliationService.ReconcileByMessageUniqueIdAsync(messageUniqueId, cancellationToken)
                    .ConfigureAwait(false);
        }

        return EmailAccStatusMapper.Map(messageUniqueId, cache, reconciliation, currentUserLogin);
    }

    public async Task<EmailAccInboxStatus?> GetStatusByInboxMessageIdAsync(
        int inboxMessageId,
        string? currentUserLogin = null,
        CancellationToken cancellationToken = default)
    {
        var cache = await _inboxQuery.GetByInboxMessageIdAsync(inboxMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (cache is null)
        {
            return null;
        }

        AccInboxReconciliationResult? reconciliation = null;
        if (_reconciliationService is not null)
        {
            reconciliation = await _reconciliationService
                .ReconcileByMessageIdAsync(inboxMessageId, cancellationToken)
                .ConfigureAwait(false);
        }

        return EmailAccStatusMapper.Map(cache.MessageUniqueId, cache, reconciliation, currentUserLogin);
    }
}
