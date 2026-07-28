using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// External-download ACC orchestration. <see cref="IEmailExternalDownloadExecutor"/> is host-supplied
/// (V2 legacy bridge); when absent (standalone New System), uploads return BackendNotAvailable.
/// </summary>
internal sealed class EmailExternalDownloadCoordinator(
    EmailAccInboxQueryService inboxQuery,
    IEmailExternalDownloadExecutor? downloadExecutor = null)
    : IEmailExternalDownloadCoordinator
{
    private readonly IEmailExternalDownloadExecutor? _downloadExecutor = downloadExecutor;
    private readonly EmailAccInboxQueryService _inboxQuery =
        inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));

    public async Task<EmailExternalDownloadResult> UploadExternalFileAsync(
        EmailExternalDownloadCommand command,
        IProgress<EmailExternalDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_downloadExecutor is null)
        {
            return EmailExternalDownloadResult.BackendNotAvailable();
        }

        return await _downloadExecutor
            .UploadExternalFileAsync(command, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<EmailExternalDownloadItem>> ListExternalDownloadsAsync(
        string? internetMessageId,
        string gmailMessageId,
        CancellationToken cancellationToken = default) =>
        _inboxQuery.ListExternalDownloadsAsync(internetMessageId, gmailMessageId, cancellationToken);
}
