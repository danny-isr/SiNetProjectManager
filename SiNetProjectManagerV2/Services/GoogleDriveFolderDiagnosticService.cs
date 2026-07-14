using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

public enum DiagnosticStatus
{
    OK,
    NotConfigured,
    GoogleNotConfigured,
    NotAuthenticated,
    NoAccess,
    NotFound,
    InvalidType,
    EmptyFolder,
    AccessibleReadOnlyOrUnknownWritePermission,
    Error
}

public class GoogleDriveFolderDiagnosticResult
{
    public DiagnosticStatus Status { get; set; }
    public string? ConnectedEmail { get; set; }
    public string? FolderIdSnippet { get; set; }
    public string? FolderName { get; set; }
    public string? WebViewLink { get; set; }
    public string? UserMessage { get; set; }
    public string? AdminHint { get; set; }
    public string? TechnicalDetails { get; set; }
}

/// <summary>
/// Diagnoses Inspection templates/reports Drive folders using the same
/// <see cref="GoogleAuthService"/> stack as template listing (not Gmail <c>GoogleService</c>).
/// </summary>
public class GoogleDriveFolderDiagnosticService
{
    private readonly GoogleAuthService _authService;

    public GoogleDriveFolderDiagnosticService(GoogleAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(
        string folderId,
        bool isTemplateFolder,
        CancellationToken ct = default,
        bool silentOnly = true)
    {
        var result = new GoogleDriveFolderDiagnosticResult();

        if (string.IsNullOrWhiteSpace(folderId))
        {
            result.Status = DiagnosticStatus.NotConfigured;
            return result;
        }

        result.FolderIdSnippet = folderId.Length > 8 ? folderId.Substring(0, 8) + "..." : folderId;

        // Health / diagnostics must never open a browser. Prefer silent restore;
        // interactive EnsureAuthenticated is only when silentOnly is explicitly false.
        bool authenticated;
        if (_authService.IsAuthenticated && _authService.DriveService is not null)
        {
            authenticated = true;
        }
        else if (silentOnly)
        {
            authenticated = await _authService.TryRestoreSessionAsync(ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                authenticated = await _authService.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.TechnicalDetails = ex.Message;
                authenticated = false;
            }
        }

        result.ConnectedEmail = await SafeGetEmailAsync().ConfigureAwait(false);

        if (!authenticated || _authService.DriveService is null)
        {
            result.Status = DiagnosticStatus.NotAuthenticated;
            return result;
        }

        var drive = _authService.DriveService;

        try
        {
            var getRequest = drive.Files.Get(folderId);
            getRequest.SupportsAllDrives = true;
            getRequest.Fields = "id, name, mimeType, webViewLink";

            var fileInfo = await getRequest.ExecuteAsync(ct).ConfigureAwait(false);

            result.FolderName = fileInfo.Name;
            result.WebViewLink = fileInfo.WebViewLink;
            result.ConnectedEmail = await SafeGetEmailAsync().ConfigureAwait(false);

            if (fileInfo.MimeType != "application/vnd.google-apps.folder")
            {
                result.Status = DiagnosticStatus.InvalidType;
                return result;
            }

            if (isTemplateFolder)
            {
                var listRequest = drive.Files.List();
                listRequest.SupportsAllDrives = true;
                listRequest.IncludeItemsFromAllDrives = true;
                listRequest.Q = $"'{folderId}' in parents and mimeType='application/vnd.google-apps.spreadsheet' and trashed=false";
                listRequest.Fields = "files(id, name)";
                listRequest.PageSize = 1;

                var listResult = await listRequest.ExecuteAsync(ct).ConfigureAwait(false);
                result.Status = GoogleDriveDiagnosticStatusMapper.MapFolderState(
                    fileInfo.MimeType,
                    listResult?.Files?.Count ?? 0,
                    isTemplateFolder);
            }
            else
            {
                result.Status = GoogleDriveDiagnosticStatusMapper.MapFolderState(
                    fileInfo.MimeType,
                    -1,
                    isTemplateFolder);
            }
        }
        catch (Exception ex)
        {
            result.Status = GoogleDriveDiagnosticStatusMapper.MapExceptionToStatus(ex);
            result.TechnicalDetails = ex.Message;
            result.ConnectedEmail = await SafeGetEmailAsync().ConfigureAwait(false);
        }

        return result;
    }

    private async Task<string> SafeGetEmailAsync()
    {
        try
        {
            var email = await _authService.GetCurrentUserEmailAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(email) ? "Unknown" : email;
        }
        catch
        {
            return "Unknown";
        }
    }
}

public static class GoogleDriveDiagnosticStatusMapper
{
    public static DiagnosticStatus MapExceptionToStatus(Exception ex)
    {
        if (ex is Google.GoogleApiException gex)
        {
            if (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden) return DiagnosticStatus.NoAccess;
            if (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) return DiagnosticStatus.NotFound;
        }
        return DiagnosticStatus.Error;
    }

    public static DiagnosticStatus MapFolderState(string mimeType, int fileCount, bool isTemplateFolder)
    {
        if (mimeType != "application/vnd.google-apps.folder")
            return DiagnosticStatus.InvalidType;

        if (isTemplateFolder)
        {
            if (fileCount == 0) return DiagnosticStatus.EmptyFolder;
            return DiagnosticStatus.OK;
        }

        return DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission;
    }
}
