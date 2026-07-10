using System.Net;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccFileDownloadService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccFileDownloadService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccFileDownloadResult?> DownloadToTempAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote file download.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildRequestUri(baseUrl, projectId, itemId));
        request.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var downloadedFileName = ResolveFileName(response, itemId);
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"acc_dl_{Guid.NewGuid():N}_{SanitizeFileName(downloadedFileName)}");

        try
        {
            await using var targetStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);

            return new AccFileDownloadResult(tempPath, downloadedFileName);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private static string BuildRequestUri(string baseUrl, string projectId, string itemId)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        return $"{trimmedBaseUrl}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId)}/items/{Uri.EscapeDataString(itemId)}/download";
    }

    private static string ResolveFileName(HttpResponseMessage response, string itemId)
    {
        if (response.Headers.TryGetValues("X-Acc-Downloaded-FileName", out var values))
        {
            var headerFileName = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerFileName))
            {
                var trimmed = headerFileName.Trim().Trim('"');
                // AccService percent-encodes non-ASCII names so the header stays ASCII-safe.
                try
                {
                    return Uri.UnescapeDataString(trimmed);
                }
                catch (UriFormatException)
                {
                    return trimmed;
                }
            }
        }

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        return string.IsNullOrWhiteSpace(fileName)
            ? itemId
            : fileName.Trim().Trim('"');
    }

    private static string SanitizeFileName(string fileName) =>
        string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}
