using Google.Apis.Drive.v3;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SiNet.Infrastructure.Google.Inspection;

/// <summary>
/// Uploads inspection-note PNG content through the shared standalone Google session.
/// Clipboard access and attachment persistence remain host responsibilities.
/// </summary>
public sealed class GoogleInspectionNoteScreenshotUploader(
    GmailClientProvider gmailClientProvider,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ISystemSettingsQueryService settings)
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string ScreenshotsFolderName = "Screenshots";

    private readonly GmailClientProvider _gmailClientProvider =
        gmailClientProvider ?? throw new ArgumentNullException(nameof(gmailClientProvider));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<GoogleInspectionScreenshotDuplicateCheck> CheckDuplicateAsync(
        long noteId,
        string contentHashSha256,
        CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            throw new ArgumentOutOfRangeException(nameof(noteId));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashSha256);

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var note = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.NoteId == noteId)
            .Select(n => new { n.ReportId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Inspection note {noteId} was not found.");

        var duplicateNoteId = await db.InspectionNoteAttachments
            .AsNoTracking()
            .Where(a => a.Note.ReportId == note.ReportId
                        && a.ContentHashSha256 == contentHashSha256)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => (long?)a.NoteId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new GoogleInspectionScreenshotDuplicateCheck(note.ReportId, duplicateNoteId);
    }

    public async Task<GoogleInspectionScreenshotUpload> UploadAndPersistAsync(
        long noteId,
        int reportId,
        string fileName,
        string contentHashSha256,
        byte[] pngContent,
        CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            throw new ArgumentOutOfRangeException(nameof(noteId));
        if (reportId <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportId));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashSha256);
        ArgumentNullException.ThrowIfNull(pngContent);
        if (pngContent.Length == 0)
            throw new ArgumentException("Screenshot content is empty.", nameof(pngContent));

        var drive = await _gmailClientProvider
            .TryGetDriveServiceAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Google Drive is unavailable. Connect the Google account and try again.");

        var settingsDto = await _settings
            .GetSystemSettingsAsync(cancellationToken)
            .ConfigureAwait(false);
        var targetFolderId = await ResolveScreenshotsFolderIdAsync(
                drive,
                reportId,
                settingsDto.Inspection.InspectionReportsFolderId,
                cancellationToken)
            .ConfigureAwait(false);

        var metadata = new DriveFile
        {
            Name = fileName,
            MimeType = "image/png",
            Parents = string.IsNullOrWhiteSpace(targetFolderId) ? null : [targetFolderId],
        };

        using var stream = new MemoryStream(pngContent, writable: false);
        var request = drive.Files.Create(metadata, stream, metadata.MimeType);
        request.SupportsAllDrives = true;
        request.Fields = "id,name,webViewLink";

        var progress = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
        if (progress.Status != global::Google.Apis.Upload.UploadStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Google Drive screenshot upload failed: {progress.Exception?.Message ?? progress.Status.ToString()}",
                progress.Exception);
        }

        var uploaded = request.ResponseBody;
        if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.Id))
            throw new InvalidOperationException("Google Drive did not return an uploaded file id.");

        var result = new GoogleInspectionScreenshotUpload(
            uploaded.Id,
            uploaded.WebViewLink ?? $"https://drive.google.com/file/d/{uploaded.Id}/view",
            uploaded.Name ?? fileName);

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        db.InspectionNoteAttachments.Add(new InspectionNoteAttachment
        {
            NoteId = noteId,
            AttachmentType = InspectionNoteAttachmentType.Screenshot,
            FileName = result.FileName,
            GoogleDriveFileId = result.GoogleDriveFileId,
            GoogleDriveUrl = result.GoogleDriveUrl,
            ContentHashSha256 = contentHashSha256,
            FileSizeBytes = pngContent.LongLength,
            UploadedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<string?> GetLastUrlAsync(
        long noteId,
        CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            throw new ArgumentOutOfRangeException(nameof(noteId));

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await db.InspectionNoteAttachments
            .AsNoTracking()
            .Where(a => a.NoteId == noteId && !string.IsNullOrWhiteSpace(a.GoogleDriveUrl))
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => a.GoogleDriveUrl)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> ResolveScreenshotsFolderIdAsync(
        DriveService drive,
        int reportId,
        string? reportsFolderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportsFolderId))
            return null;

        await using var db = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var report = await db.InspectionReports
            .AsNoTracking()
            .Include(r => r.Project)
                .ThenInclude(p => p!.Place)
            .Include(r => r.Project)
                .ThenInclude(p => p!.OnerProject)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);

        var folderId = reportsFolderId.Trim();
        if (report?.Project?.Place?.Title is { Length: > 0 } location)
            folderId = await FindOrCreateFolderAsync(drive, folderId, location, cancellationToken).ConfigureAwait(false);

        if (report?.Project?.OnerProject is { } parent)
        {
            var parentName = parent.Title ?? $"Project_{report.Project.OnerProjectId}";
            folderId = await FindOrCreateFolderAsync(drive, folderId, parentName, cancellationToken).ConfigureAwait(false);
        }

        if (report?.Project is { } project)
        {
            var projectName = project.Title ?? $"Project_{project.Id}";
            folderId = await FindOrCreateFolderAsync(drive, folderId, projectName, cancellationToken).ConfigureAwait(false);
        }

        return await FindOrCreateFolderAsync(
                drive,
                folderId,
                ScreenshotsFolderName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> FindOrCreateFolderAsync(
        DriveService drive,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken)
    {
        var list = drive.Files.List();
        list.Q =
            $"'{EscapeQueryValue(parentFolderId)}' in parents " +
            $"and mimeType = '{FolderMimeType}' " +
            $"and name = '{EscapeQueryValue(folderName)}' " +
            "and trashed = false";
        list.Fields = "files(id,name)";
        list.PageSize = 1;
        list.SupportsAllDrives = true;
        list.IncludeItemsFromAllDrives = true;

        var existing = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Files is { Count: > 0 })
            return existing.Files[0].Id;

        var create = drive.Files.Create(new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = [parentFolderId],
        });
        create.SupportsAllDrives = true;
        create.Fields = "id";

        var created = await create.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return created.Id;
    }

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
}

public sealed record GoogleInspectionScreenshotUpload(
    string GoogleDriveFileId,
    string GoogleDriveUrl,
    string FileName);

public sealed record GoogleInspectionScreenshotDuplicateCheck(
    int ReportId,
    long? DuplicateNoteId);
