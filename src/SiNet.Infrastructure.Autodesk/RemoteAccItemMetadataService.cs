using System.Net.Http.Json;
using SiOffice.AccService.Contracts;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Autodesk.Metadata;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// HTTP client for <see cref="IAccItemMetadataService"/> — the "Remote" side of the ACC
/// control-plane separation. Used by the WPF client (which does NOT hold Autodesk credentials):
/// custom-attribute read/write is forwarded to <c>SiOffice.AccService</c>, which performs the
/// privileged ACC SDK calls server-side. This keeps the structural client/server boundary intact.
/// <para>
/// Metadata-only semantics: ordinary ACC / transport failures are surfaced as failed results
/// (never thrown) so a metadata failure is never mistaken for "the ACC file is missing".
/// </para>
/// </summary>
internal sealed class RemoteAccItemMetadataService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider,
    IAppLogger logger) : IAccItemMetadataService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<AccItemMetadataReadResult> ReadAttributesAsync(
        string accProjectId,
        string itemId,
        string? fileNameForLogging,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            return AccItemMetadataReadResult.Fail(null, "accProjectId is required.");
        if (string.IsNullOrWhiteSpace(itemId))
            return AccItemMetadataReadResult.Fail(null, "itemId is required.");

        try
        {
            using var request = BuildRequest(HttpMethod.Get, accProjectId, itemId);
            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn(
                    $"[AccMetadata] op=Read outcome=Failed item={itemId} file='{fileNameForLogging}' http={(int)response.StatusCode}");
                return AccItemMetadataReadResult.Fail(
                    (int)response.StatusCode,
                    $"ACC service returned {(int)response.StatusCode} for custom-attributes read.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<RemoteAccItemCustomAttributesReadResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                _logger.Warn($"[AccMetadata] op=Read outcome=Failed item={itemId} file='{fileNameForLogging}' detail=empty response");
                return AccItemMetadataReadResult.Fail(null, "ACC service returned an empty custom-attributes read response.");
            }

            if (payload.Success)
            {
                return AccItemMetadataReadResult.Ok(
                    payload.Attributes ?? new Dictionary<string, string?>(StringComparer.Ordinal));
            }

            _logger.Warn(
                $"[AccMetadata] op=Read outcome=Failed item={itemId} file='{fileNameForLogging}' http={payload.HttpStatus} detail={payload.ErrorMessage}");
            return AccItemMetadataReadResult.Fail(
                payload.HttpStatus,
                payload.ErrorMessage ?? "Unknown metadata read error.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"[AccMetadata] op=Read outcome=Failed item={itemId} file='{fileNameForLogging}' detail={ex.Message}");
            return AccItemMetadataReadResult.Fail(null, $"Remote metadata read failed: {ex.Message}");
        }
    }

    public async ValueTask<AccItemMetadataResult> WriteAttributesAsync(
        string accProjectId,
        string accFolderId,
        string versionId,
        string itemId,
        IReadOnlyDictionary<string, string?> attributes,
        string? fileNameForLogging,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            return AccItemMetadataResult.Fail(null, "accProjectId is required.");
        if (string.IsNullOrWhiteSpace(accFolderId))
            return AccItemMetadataResult.Fail(null, "accFolderId is required.");
        if (string.IsNullOrWhiteSpace(versionId))
            return AccItemMetadataResult.Fail(null, "AccVersionId is required for ACC custom attribute writes.");
        if (attributes is null || attributes.Count == 0)
            return AccItemMetadataResult.Ok();

        try
        {
            using var request = BuildRequest(HttpMethod.Post, accProjectId, itemId);
            request.Content = JsonContent.Create(new RemoteAccItemMetadataWriteRequest(
                accFolderId.Trim(),
                versionId.Trim(),
                attributes));

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn(
                    $"[AccMetadata] op=Write outcome=Failed item={itemId} file='{fileNameForLogging}' http={(int)response.StatusCode}");
                return AccItemMetadataResult.Fail(
                    (int)response.StatusCode,
                    $"ACC service returned {(int)response.StatusCode} for custom-attributes write.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<RemoteAccItemMetadataWriteResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                _logger.Warn($"[AccMetadata] op=Write outcome=Failed item={itemId} file='{fileNameForLogging}' detail=empty response");
                return AccItemMetadataResult.Fail(null, "ACC service returned an empty custom-attributes write response.");
            }

            if (!payload.Success)
            {
                _logger.Warn(
                    $"[AccMetadata] op=Write outcome=Failed item={itemId} file='{fileNameForLogging}' http={payload.HttpStatus} detail={payload.ErrorMessage}");
                return AccItemMetadataResult.Fail(payload.HttpStatus, payload.ErrorMessage ?? "Unknown metadata write error.");
            }

            return AccItemMetadataResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"[AccMetadata] op=Write outcome=Failed item={itemId} file='{fileNameForLogging}' detail={ex.Message}");
            return AccItemMetadataResult.Fail(null, $"Remote metadata write failed: {ex.Message}");
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string projectId, string itemId)
    {
        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.Error("[AccRemote] missing BaseUrl op=AccMetadata");
            throw new InvalidOperationException("ACC service base URL is not configured for remote metadata operations.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.Error("[AccRemote] missing ApiKey op=AccMetadata");
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}{AccServiceContracts.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId.Trim())}/items/{Uri.EscapeDataString(itemId.Trim())}/custom-attributes";
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);
        return request;
    }
}

internal sealed record RemoteAccItemMetadataWriteRequest(
    string AccFolderId,
    string VersionId,
    IReadOnlyDictionary<string, string?> Attributes);

internal sealed record RemoteAccItemCustomAttributesReadResponse(
    bool Success,
    int? HttpStatus,
    string? ErrorMessage,
    Dictionary<string, string?>? Attributes);

internal sealed record RemoteAccItemMetadataWriteResponse(
    bool Success,
    int? HttpStatus,
    string? ErrorMessage);
