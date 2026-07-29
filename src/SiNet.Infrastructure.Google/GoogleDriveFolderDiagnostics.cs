using System.Net;
using Google;
using Google.Apis.Drive.v3;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Google;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Drive folder / Shared Drive diagnostics over the shared Gmail/Drive credential
/// (see <c>docs/SYSTEM_HEALTH.md</c> §2.4).
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
        bool requireWriteAccess = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.NotConfigured);

        var trimmed = folderId.Trim();
        var snippet = Snippet(trimmed);
        var email = _auth.ConnectedAccountEmail;

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
            get.Fields = "id, name, mimeType, webViewLink, capabilities(canAddChildren,canEdit)";

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

            if (requireWriteAccess)
            {
                var canAdd = info.Capabilities?.CanAddChildren;
                if (canAdd != true)
                {
                    return new GoogleDriveFolderDiagnosticResult(
                        GoogleDriveFolderStatus.NoWriteAccess,
                        email,
                        snippet,
                        info.Name,
                        info.WebViewLink,
                        TechnicalDetails: canAdd is null
                            ? "capabilities.canAddChildren omitted"
                            : "capabilities.canAddChildren=false");
                }
            }

            if (expectSpreadsheets)
            {
                var hasSheet = await HasAnySpreadsheetAsync(drive, trimmed, cancellationToken)
                    .ConfigureAwait(false);
                return new GoogleDriveFolderDiagnosticResult(
                    hasSheet ? GoogleDriveFolderStatus.Ok : GoogleDriveFolderStatus.EmptyFolder,
                    email,
                    snippet,
                    info.Name,
                    info.WebViewLink);
            }

            // Writable path already returned Ok implicitly by not failing above.
            if (requireWriteAccess)
            {
                return new GoogleDriveFolderDiagnosticResult(
                    GoogleDriveFolderStatus.Ok,
                    email,
                    snippet,
                    info.Name,
                    info.WebViewLink);
            }

            return new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.ReadOnlyOrUnknownWrite,
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

    public async Task<GoogleDriveFolderDiagnosticResult> DiagnoseSharedDriveWriteAsync(
        string? sharedDriveId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedDriveId))
            return new GoogleDriveFolderDiagnosticResult(GoogleDriveFolderStatus.NotConfigured);

        var trimmed = sharedDriveId.Trim();
        var snippet = Snippet(trimmed);
        var email = _auth.ConnectedAccountEmail;

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
            // Same probe as NativeReportsDriveHelper.CheckWriteAccessAsync — keep them aligned.
            var get = drive.Drives.Get(trimmed);
            get.Fields = "id, name, capabilities(canAddChildren)";
            var shared = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            if (shared.Capabilities?.CanAddChildren == true)
            {
                return new GoogleDriveFolderDiagnosticResult(
                    GoogleDriveFolderStatus.Ok,
                    email,
                    snippet,
                    shared.Name);
            }

            return new GoogleDriveFolderDiagnosticResult(
                GoogleDriveFolderStatus.NoWriteAccess,
                email,
                snippet,
                shared.Name,
                TechnicalDetails: shared.Capabilities?.CanAddChildren is null
                    ? "capabilities.canAddChildren omitted"
                    : "capabilities.canAddChildren=false");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[DriveDiagnostics] Shared Drive {snippet} write probe failed: {ex.Message}");
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

    private static string Snippet(string id) => id.Length > 8 ? id[..8] + "..." : id;

    private static GoogleDriveFolderStatus MapException(Exception ex) => ex switch
    {
        GoogleApiException { HttpStatusCode: HttpStatusCode.Forbidden } => GoogleDriveFolderStatus.NoAccess,
        GoogleApiException { HttpStatusCode: HttpStatusCode.NotFound } => GoogleDriveFolderStatus.NotFound,
        _ => GoogleDriveFolderStatus.Error,
    };
}
