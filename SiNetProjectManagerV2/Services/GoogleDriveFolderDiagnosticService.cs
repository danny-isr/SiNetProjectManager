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
    private readonly GoogleAuthService _authService;

    public GoogleDriveFolderDiagnosticService(GoogleAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(string folderId, bool isTemplateFolder, CancellationToken ct = default)
    {
        var result = new GoogleDriveFolderDiagnosticResult();

        if (string.IsNullOrWhiteSpace(folderId))
        {
            result.Status = DiagnosticStatus.NotConfigured;
            return result;
        }

        result.FolderIdSnippet = folderId.Length > 8 ? folderId.Substring(0, 8) + "..." : folderId;

        try
        {
            result.ConnectedEmail = await _authService.GetCurrentUserEmailAsync();
        }
        catch
        {
            result.ConnectedEmail = "Unknown";
        }

        if (!_authService.IsAuthenticated)
        {
            bool ok = false;
            try
            {
                ok = await _authService.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
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
                if (listResult.Files == null || listResult.Files.Count == 0)
                {
                    result.Status = DiagnosticStatus.EmptyFolder;
                    return result;
                }
                
                result.Status = DiagnosticStatus.OK;
            }
            else
            {
                // For reports folder, we just test access. Write validation is postponed.
                result.Status = DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission;
            }
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            result.Status = DiagnosticStatus.NoAccess;
            result.TechnicalDetails = "403 Forbidden: " + ex.Message;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            result.Status = DiagnosticStatus.NotFound;
            result.TechnicalDetails = "404 Not Found: " + ex.Message;
        }
        catch (Exception ex)
        {
            result.Status = DiagnosticStatus.Error;
            result.TechnicalDetails = ex.Message;
        }

        return result;
    }
}
