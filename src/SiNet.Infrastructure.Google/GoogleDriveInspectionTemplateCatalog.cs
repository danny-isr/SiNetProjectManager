using Google.Apis.Drive.v3;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Lists Google Sheets inspection templates from the admin-configured Drive folder
/// using the native shared Google session (<see cref="GmailClientProvider"/>).
/// List-only — does not create/export reports.
/// </summary>
public sealed class GoogleDriveInspectionTemplateCatalog(
    GmailClientProvider gmailClientProvider,
    ISystemSettingsQueryService systemSettings,
    IAppLogger? logger = null) : IInspectionTemplateCatalog
{
    private const string SpreadsheetMimeType = "application/vnd.google-apps.spreadsheet";

    private readonly GmailClientProvider _gmailClientProvider =
        gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly ISystemSettingsQueryService _systemSettings =
        systemSettings ?? throw new ArgumentNullException(nameof(systemSettings));
    private readonly IAppLogger? _logger = logger;

    public async Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var dto = await _systemSettings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folderId = dto.Inspection.InspectionTemplatesFolderId;
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return [];
        }

        var drive = await _gmailClientProvider.TryGetDriveServiceAsync(cancellationToken).ConfigureAwait(false);
        if (drive is null)
        {
            _logger?.Warn(
                "[GoogleDriveInspectionTemplateCatalog] Drive session unavailable; template list empty.");
            return [];
        }

        try
        {
            return await ListSheetsInFolderAsync(drive, folderId.Trim(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.Error("[GoogleDriveInspectionTemplateCatalog] Failed to list templates", ex);
            return [];
        }
    }

    private static async Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListSheetsInFolderAsync(
        DriveService driveService,
        string folderId,
        CancellationToken cancellationToken)
    {
        var templates = new List<InspectionTemplateCatalogItem>();
        string? pageToken = null;

        do
        {
            var request = driveService.Files.List();
            request.Q =
                $"'{folderId}' in parents " +
                $"and mimeType = '{SpreadsheetMimeType}' " +
                "and trashed = false";
            request.Fields = "nextPageToken, files(id, name, webViewLink)";
            request.OrderBy = "name";
            request.PageSize = 100;
            request.PageToken = pageToken;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (result.Files is not null)
            {
                foreach (var file in result.Files)
                {
                    if (string.IsNullOrWhiteSpace(file.Id))
                    {
                        continue;
                    }

                    var url = file.WebViewLink
                              ?? $"https://docs.google.com/spreadsheets/d/{file.Id}";
                    templates.Add(new InspectionTemplateCatalogItem(
                        file.Name ?? file.Id,
                        file.Id,
                        url));
                }
            }

            pageToken = result.NextPageToken;
        }
        while (pageToken is not null);

        return templates;
    }
}
