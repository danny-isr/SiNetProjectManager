using System.Net;
using Google;
using Google.Apis.Drive.v3;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Google;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Drive folder diagnostics over the shared Gmail/Drive credential. Ported from the legacy
/// <c>GoogleDriveFolderDiagnosticService</c> so the standalone host can report folder permission
/// problems without referencing V2 (see <c>docs/SYSTEM_HEALTH.md</c> §2.4).
/// </summary>
public sealed class GoogleDriveFolderDiagnostics : IGoogleDriveFolderDiagnostics
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string SpreadsheetMimeType = "application/vnd.google-apps.spreadsheet";

    private readonly GmailClientProvider _clientProvider;
    private readonly IConnectorAuthService _auth;
    private readonly IAppLogger? _logger;

    public GoogleDriveFolderDiagnostics(
        GmailClientProvider clientProvider,
        IConnectorAuthService auth,
        IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clientProvider);
        ArgumentNullException.ThrowIfNull(auth);

        _clientProvider = clientProvider;
        _auth = auth;
        _logger = logger;
    }

    public async Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(
        string? folderId,
        bool expectSpreadsheets,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.NotConfigured);

        var trimmed = folderId.Trim();
        var snippet = trimmed.Length > 8 ? trimmed[..8] + "..." : trimmed;
        var email = _auth.ConnectedAccountEmail;

        // TryGetDriveServiceAsync reuses the existing session and never prompts.
        var drive = await _clientProvider.TryGetDriveServiceAsync(cancellationToken).ConfigureAwait(false);
        if (drive is null)
        {
            return new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.NotAuthenticated,
                email,
                snippet);
        }

        try
        {
            var get = drive.Files.Get(trimmed);
            get.SupportsAllDrives = true;
            get.Fields = "id, name, mimeType, webViewLink";

            var info = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (!string.Equals(info.MimeType, FolderMimeType, StringComparison.Ordinal))
            {
                return new GoogleDriveFolderDiagnosticResult(
                    GoogleDriveFolderStatus.InvalidType,
                    email,
                    snippet,
                    info.Name,
                    info.WebViewLink);
            }

            if (!expectSpreadsheets)
            {
                return new GoogleDriveFolderDiagnosticResult(
                    GoogleDriveFolderStatus.ReadOnlyOrUnknownWrite,
                    email,
                    snippet,
                    info.Name,
                    info.WebViewLink);
            }

            var status = await HasAnySpreadsheetAsync(drive, trimmed, cancellationToken).ConfigureAwait(false)
                ? GoogleDriveFolderStatus.Ok
                : GoogleDriveFolderStatus.EmptyFolder;

            return new GoogleDriveFolderDiagnosticResult(
                status,
                email,
                snippet,
                info.Name,
                info.WebViewLink);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[DriveDiagnostics] Folder {snippet} probe failed: {ex.Message}");
            return new GoogleDriveFolderDiagnosticResult(
                MapException(ex),
                email,
                snippet,
                TechnicalDetails: ex.Message);
        }
    }

    private static async Task<bool> HasAnySpreadsheetAsync(
        DriveService drive,
        string folderId,
        CancellationToken cancellationToken)
    {
        var list = drive.Files.List();
        list.SupportsAllDrives = true;
        list.IncludeItemsFromAllDrives = true;
        list.Q = $"'{folderId}' in parents and mimeType='{SpreadsheetMimeType}' and trashed=false";
        list.Fields = "files(id, name)";
        list.PageSize = 1;

        var result = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result?.Files?.Count > 0;
    }

    private static GoogleDriveFolderStatus MapException(Exception ex) => ex switch
    {
        GoogleApiException { HttpStatusCode: HttpStatusCode.Forbidden } => GoogleDriveFolderStatus.NoAccess,
        GoogleApiException { HttpStatusCode: HttpStatusCode.NotFound } => GoogleDriveFolderStatus.NotFound,
        _ => GoogleDriveFolderStatus.Error,
    };
}
