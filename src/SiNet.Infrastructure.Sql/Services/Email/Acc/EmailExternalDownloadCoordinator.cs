using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

internal sealed class EmailExternalDownloadCoordinator(
    IEmailExternalDownloadExecutor? downloadExecutor,
    EmailAccInboxQueryService inboxQuery)
    : IEmailExternalDownloadCoordinator
{
    private readonly IEmailExternalDownloadExecutor? _downloadExecutor = downloadExecutor;
    private readonly EmailAccInboxQueryService _inboxQuery =
        inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));

    public async Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_downloadExecutor is null)
        {
            return EmailExternalDownloadResult.BackendNotAvailable();
        }

        return await _downloadExecutor
            .UploadExternalFileAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<EmailExternalDownloadItem>> ListExternalDownloadsAsync(
        string? internetMessageId,
        string gmailMessageId,
        CancellationToken cancellationToken = default) =>
        _inboxQuery.ListExternalDownloadsAsync(internetMessageId, gmailMessageId, cancellationToken);
}
