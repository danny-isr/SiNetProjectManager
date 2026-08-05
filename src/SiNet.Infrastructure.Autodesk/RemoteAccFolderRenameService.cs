using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiOffice.AccService.Contracts;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Remote AccService adapter for ACC Docs folder rename (DEV-008 Layer A).
/// </summary>
internal sealed class RemoteAccFolderRenameService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccFolderRenameService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccFolderRenameOutcome> RenameFolderAsync(
        string accProjectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accProjectId)
            || string.IsNullOrWhiteSpace(folderId)
            || string.IsNullOrWhiteSpace(newFolderName))
        {
            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Failed,
                "חסרים מזהה פרויקט ACC, מזהה תיקייה או שם חדש.");
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Failed,
                "AccService BaseUrl לא מוגדר לשינוי שם תיקייה מרחוק.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Failed,
                "מפתח AccService לא מוגדר ב-vault.");
        }

        var requestUri =
            $"{baseUrl.TrimEnd('/')}{AccServiceContracts.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(accProjectId.Trim())}/folders/{Uri.EscapeDataString(folderId.Trim())}/rename";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new RenameFolderRequest(newFolderName.Trim())),
            };
            request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new AccFolderRenameOutcome(
                    AccFolderRenameStatus.Failed,
                    $"AccService rename נכשל: HTTP {(int)response.StatusCode} {detail}");
            }

            return new AccFolderRenameOutcome(
                AccFolderRenameStatus.Succeeded,
                $"ACC Docs (Remote): שם התיקייה עודכן ל־'{newFolderName.Trim()}'");
        }
        catch (HttpRequestException ex)
        {
            return new AccFolderRenameOutcome(AccFolderRenameStatus.Failed, ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new AccFolderRenameOutcome(AccFolderRenameStatus.Failed, $"Timeout: {ex.Message}");
        }
    }
}
