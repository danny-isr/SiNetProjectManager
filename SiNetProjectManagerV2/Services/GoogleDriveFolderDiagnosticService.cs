using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Requests;
using SiNetSQL.Services;
using SiOffice.GoogleConnector;

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

public class GoogleDriveFolderDiagnosticService
{
    private readonly GoogleService _authService;

    public GoogleDriveFolderDiagnosticService(GoogleService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(string folderId, bool isTemplateFolder, CancellationToken ct = default, bool silentOnly = false)
    {
        var result = new GoogleDriveFolderDiagnosticResult();

        if (string.IsNullOrWhiteSpace(folderId))
        {
            result.Status = DiagnosticStatus.NotConfigured;
            return result;
        }

        result.FolderIdSnippet = folderId.Length > 8 ? folderId.Substring(0, 8) + "..." : folderId;

        result.ConnectedEmail = string.IsNullOrWhiteSpace(_authService.CurrentUserEmail) ? "Unknown" : _authService.CurrentUserEmail;

        if (!_authService.IsAuthenticated)
        {
            bool ok = false;
            try
            {
                var credentialsPath = AppConfiguration.GetGoogleClientSecretsPath() ?? "client_secrets.json";
                ok = await _authService.TryRestoreSessionAsync(credentialsPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.TechnicalDetails = ex.Message;
            }

            if (!ok)
            {
                result.Status = DiagnosticStatus.NotAuthenticated;
                return result;
            }
            
            result.ConnectedEmail = string.IsNullOrWhiteSpace(_authService.CurrentUserEmail) ? "Unknown" : _authService.CurrentUserEmail;
        }

        var drive = _authService.DriveService;
        if (drive == null)
        {
            result.Status = DiagnosticStatus.GoogleNotConfigured;
            return result;
        }

        try
        {
            var getRequest = drive.Files.Get(folderId);
            getRequest.SupportsAllDrives = true;
            getRequest.Fields = "id, name, mimeType, webViewLink";

            var fileInfo = await getRequest.ExecuteAsync(ct);

            result.FolderName = fileInfo.Name;
            result.WebViewLink = fileInfo.WebViewLink;

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

                var listResult = await listRequest.ExecuteAsync(ct);
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
        }

        return result;
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
