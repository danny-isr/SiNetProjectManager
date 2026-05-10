using System.IO;
using Google.Apis.Drive.v3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNetSQL.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Google Drive implementation of <see cref="INoteScreenshotUploadService"/>.
/// Uploads inspection-note screenshots to a per-project sub-folder named
/// <c>Screenshots</c> inside the same folder hierarchy used by
/// <see cref="GoogleReportExportService"/> (Reports / Location / [Parent] / Project).
/// Image bytes are NOT persisted in the DB ג€” only Drive metadata is returned.
/// </summary>
public sealed class GoogleNoteScreenshotUploadService : INoteScreenshotUploadService
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string ScreenshotsFolderName = "Screenshots";

    private readonly GoogleAuthService _authService;
    private readonly IDbContextFactory<SiNetSQLDbContext> _contextFactory;
    private readonly ILogger<GoogleNoteScreenshotUploadService>? _logger;

    /// <summary>Root "Reports" folder ID injected from system settings.</summary>
    public string? ReportsFolderId { get; set; }

    public GoogleNoteScreenshotUploadService(
        GoogleAuthService authService,
        IDbContextFactory<SiNetSQLDbContext> contextFactory,
        ILogger<GoogleNoteScreenshotUploadService>? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger;
    }

    public async Task<NoteScreenshotUploadResult> UploadScreenshotAsync(
        int reportId,
        long noteId,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Failure("׳©׳ ׳§׳•׳‘׳¥ ׳¨׳™׳§");
        if (content == null || content.Length == 0)
            return Failure("׳×׳•׳›׳ ׳”׳§׳•׳‘׳¥ ׳¨׳™׳§");

        try
        {
            await _authService.EnsureAuthenticatedAsync(cancellationToken);
            var driveService = _authService.DriveService
                ?? throw new InvalidOperationException("Drive service not available after authentication.");

            // Resolve the same folder hierarchy used by export, then a Screenshots sub-folder.
            string? targetFolderId = await ResolveScreenshotsFolderIdAsync(driveService, reportId, cancellationToken);

            var meta = new DriveFile
            {
                Name = fileName,
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType
            };
            if (!string.IsNullOrWhiteSpace(targetFolderId))
                meta.Parents = [targetFolderId];

            using var ms = new MemoryStream(content);
            var createRequest = driveService.Files.Create(meta, ms, meta.MimeType);
            createRequest.SupportsAllDrives = true;
            createRequest.Fields = "id, name, webViewLink";

            var progress = await createRequest.UploadAsync(cancellationToken);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                var msg = progress.Exception?.Message ?? progress.Status.ToString();
                _logger?.LogError(progress.Exception, "[NoteScreenshot] Upload failed: {Status}", msg);
                return Failure($"׳”׳¢׳׳׳” ׳׳›׳•׳ ׳ ׳ ׳›׳©׳׳”: {msg}");
            }

            var uploaded = createRequest.ResponseBody;
            if (uploaded == null || string.IsNullOrWhiteSpace(uploaded.Id))
                return Failure("׳׳ ׳”׳×׳§׳‘׳ ׳׳–׳”׳” ׳§׳•׳‘׳¥ ׳-Drive");

            return new NoteScreenshotUploadResult
            {
                IsSuccess = true,
                GoogleDriveFileId = uploaded.Id,
                GoogleDriveUrl = uploaded.WebViewLink
                    ?? $"https://drive.google.com/file/d/{uploaded.Id}/view",
                FileName = uploaded.Name ?? fileName,
                FolderId = targetFolderId
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[NoteScreenshot] Upload threw for ReportId={ReportId} NoteId={NoteId}", reportId, noteId);
            return Failure(ex.Message);
        }
    }

    private async Task<string?> ResolveScreenshotsFolderIdAsync(
        DriveService driveService,
        int reportId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ReportsFolderId))
            return null;

        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var report = await ctx.InspectionReports
                .AsNoTracking()
                .Include(r => r.Project)
                    .ThenInclude(p => p!.Place)
                .Include(r => r.Project)
                    .ThenInclude(p => p!.OnerProject)
                .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);

            if (report == null)
                return ReportsFolderId;

            string folderId = ReportsFolderId!;
            var location = report.Project?.Place?.Title;
            if (!string.IsNullOrWhiteSpace(location))
                folderId = await FindOrCreateDriveFolderAsync(driveService, folderId, location!, cancellationToken);

            if (report.Project?.OnerProject != null)
            {
                var parentName = report.Project.OnerProject.Title ?? $"Project_{report.Project.OnerProjectId}";
                folderId = await FindOrCreateDriveFolderAsync(driveService, folderId, parentName, cancellationToken);
            }

            if (report.Project != null)
            {
                var projectName = report.Project.Title ?? $"Project_{report.Project.Id}";
                folderId = await FindOrCreateDriveFolderAsync(driveService, folderId, projectName, cancellationToken);
            }

            folderId = await FindOrCreateDriveFolderAsync(driveService, folderId, ScreenshotsFolderName, cancellationToken);
            return folderId;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[NoteScreenshot] Failed to resolve folder hierarchy ג€” falling back to ReportsFolderId.");
            return ReportsFolderId;
        }
    }

    private static async Task<string> FindOrCreateDriveFolderAsync(
        DriveService driveService,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken)
    {
        var listRequest = driveService.Files.List();
        listRequest.Q = $"'{parentFolderId}' in parents " +
                        $"and mimeType = '{FolderMimeType}' " +
                        $"and name = '{folderName.Replace("'", "\\'")}' " +
                        $"and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.PageSize = 1;
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;

        var listResult = await listRequest.ExecuteAsync(cancellationToken);
        if (listResult.Files is { Count: > 0 })
            return listResult.Files[0].Id;

        var folderMeta = new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = [parentFolderId]
        };

        var createRequest = driveService.Files.Create(folderMeta);
        createRequest.SupportsAllDrives = true;
        createRequest.Fields = "id";

        var created = await createRequest.ExecuteAsync(cancellationToken);
        return created.Id;
    }

    private static NoteScreenshotUploadResult Failure(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}
