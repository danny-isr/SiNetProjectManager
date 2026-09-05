using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.ProjectWork;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;
using NativeActiveFileInfo = SiNet.Application.ProjectWork.ActiveFileInfo;
using NativeActiveFolderInfo = SiNet.Application.ProjectWork.ActiveFolderInfo;
using LegacyActiveFileInfo = SiNetSQL.Services.ActiveFileQuery.ActiveFileInfo;
using LegacyActiveFolderInfo = SiNetSQL.Services.ActiveFileQuery.ActiveFolderInfo;
using LegacyActiveAlternativeInfo = SiNetSQL.Services.ActiveFileQuery.ActiveAlternativeInfo;
using LegacyActiveVersionInfo = SiNetSQL.Services.ActiveFileQuery.ActiveVersionInfo;

namespace SiNetProjectManagerV2.Services;

/// <summary>Lists Google Sheets templates from the admin-configured Drive folder.</summary>
internal sealed class V2InspectionTemplateCatalog(
    GoogleAuthService authService,
    ISystemSettingsQueryService settings) : IInspectionTemplateCatalog
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folderId = dto.Inspection.InspectionTemplatesFolderId;
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return [];
        }

        var provider = new GoogleInspectionTemplateProvider(_authService);
        var items = await provider
            .GetAvailableTemplatesAsync(folderId, cancellationToken)
            .ConfigureAwait(false);

        return items
            .Select(t => new InspectionTemplateCatalogItem(t.Name, t.SpreadsheetId, t.Url))
            .ToList();
    }
}

/// <summary>
/// V2 host bridge: create-report via legacy Google template sync + <see cref="IInspectionReportService"/>;
/// other commands forward to <see cref="SqlInspectionReportCommandService"/>.
/// </summary>
internal sealed class V2InspectionReportCommandService(
    SqlInspectionReportCommandService inner,
    IInspectionReportService reportService,
    TemplateSyncService templateSync,
    GoogleAuthService authService) : IInspectionReportCommandService
{
    private readonly SqlInspectionReportCommandService _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IInspectionReportService _reportService =
        reportService ?? throw new ArgumentNullException(nameof(reportService));
    private readonly TemplateSyncService _templateSync =
        templateSync ?? throw new ArgumentNullException(nameof(templateSync));
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));

    public async Task<InspectionReportCommandResult> CreateReportAsync(
        int projectId,
        string templateUrl,
        int? seriesId = null,
        string? inspectorName = null,
        int? inspectorId = null,
        string? spreadsheetId = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return InspectionReportCommandResult.Fail("לא נבחר פרויקט.");
        }

        if (string.IsNullOrWhiteSpace(templateUrl))
        {
            return InspectionReportCommandResult.Fail("יש לבחור תבנית לפני יצירת דוח.");
        }

        var sheetId = spreadsheetId;
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            sheetId = ExtractSpreadsheetId(templateUrl);
        }

        if (string.IsNullOrWhiteSpace(sheetId))
        {
            return InspectionReportCommandResult.Fail("לא ניתן לחלץ מזהה תבנית מהקישור.");
        }

        try
        {
            var provider = new GoogleInspectionTemplateProvider(_authService);

            var resolvedSeriesId = seriesId;
            if (resolvedSeriesId is null or <= 0)
            {
                resolvedSeriesId = await _templateSync
                    .EnsureSeriesAsync(projectId, sheetId, templateUrl, cancellationToken)
                    .ConfigureAwait(false);
            }

            var scanResult = await provider
                .ScanAndParseTemplateAsync(sheetId, cancellationToken)
                .ConfigureAwait(false);

            if (scanResult.HasErrors)
            {
                var summary = string.Join("; ", scanResult.ValidationErrors.Take(3).Select(e => e.Message));
                return InspectionReportCommandResult.Fail($"שגיאות בתבנית: {summary}");
            }

            if (scanResult.SyncRows.Count == 0)
            {
                return InspectionReportCommandResult.Fail(
                    "לא נמצאו שורות בתבנית — ודא שהגיליון מכיל פרקים וסעיפים.");
            }

            var syncResult = await _templateSync
                .SyncAsync(scanResult.SyncRows, resolvedSeriesId, cancellationToken)
                .ConfigureAwait(false);

            if (syncResult.HasErrors)
            {
                var summary = string.Join("; ", syncResult.Errors.Take(3));
                return InspectionReportCommandResult.Fail($"שגיאות בסנכרון תבנית: {summary}");
            }

            var report = await _reportService
                .CreateReportAsync(
                    projectId,
                    templateUrl,
                    inspectorName,
                    cancellationToken,
                    inspectorId,
                    resolvedSeriesId)
                .ConfigureAwait(false);

            return InspectionReportCommandResult.Ok(report.ReportId);
        }
        catch (Exception ex)
        {
            return InspectionReportCommandResult.Fail($"שגיאה ביצירת דוח: {ex.Message}");
        }
    }

    public Task<InspectionReportCommandResult> UnlockReportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        _inner.UnlockReportAsync(reportId, cancellationToken);

    public Task<InspectionReportCommandResult> HydrateEmptyReportFromTemplateAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        _inner.HydrateEmptyReportFromTemplateAsync(reportId, cancellationToken);

    public Task<InspectionReportCommandResult> DeleteReportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        _inner.DeleteReportAsync(reportId, cancellationToken);

    public Task<InspectionReportCommandResult> SetReviewedVersionAsync(
        int reportId, string? reviewedVersion, CancellationToken cancellationToken = default) =>
        _inner.SetReviewedVersionAsync(reportId, reviewedVersion, cancellationToken);

    public Task<InspectionReportCommandResult> ReplaceReviewedFilesAsync(
        int reportId,
        IReadOnlyList<InspectionReviewedFileRow> files,
        CancellationToken cancellationToken = default) =>
        _inner.ReplaceReviewedFilesAsync(reportId, files, cancellationToken);

    private static string? ExtractSpreadsheetId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        if (!urlOrId.Contains('/'))
        {
            return urlOrId;
        }

        var match = Regex.Match(urlOrId, @"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : urlOrId;
    }
}

/// <summary>V2 host adapters for Inspection file picker — native ProjectWork hubs + legacy FileTreePicker UI.</summary>
internal sealed class V2InspectionFileTreePickerHost(IActiveFileQueryHub activeFiles) : IInspectionFileTreePickerHost
{
    private readonly IActiveFileQueryHub _activeFiles =
        activeFiles ?? throw new ArgumentNullException(nameof(activeFiles));

    public async Task<IReadOnlyList<InspectionFilePickResult>?> PickReviewedPlansAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        _ = projectId;
        if (!_activeFiles.IsAvailable)
            return null;

        var tree = NativeActiveFileTreeMapper.ToLegacy(_activeFiles.GetActiveFolderTree());
        var picker = new Inspection.FileTreePicker();
        var picked = await picker.PickAsync(new FileTreePickerRequest
        {
            Title = "בחר תוכניות שנבדקו",
            Purpose = "ReviewedPlans",
            SelectionMode = FilePickerSelectionMode.Multiple,
            Tree = tree,
        }, cancellationToken).ConfigureAwait(true);

        if (picked is null)
            return null;

        return picked
            .Select(p => new InspectionFilePickResult(
                p.FileName,
                string.IsNullOrWhiteSpace(p.Alternative) ? null : p.Alternative,
                Version: null,
                FullPath: null))
            .ToList();
    }

    public async Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        _ = projectId;
        if (!_activeFiles.IsAvailable)
            return null;

        var tree = NativeActiveFileTreeMapper.ToLegacy(_activeFiles.GetActiveFolderTree());
        var picker = new Inspection.FileTreePicker();
        var picked = await picker.PickAsync(new FileTreePickerRequest
        {
            Title = "בחר קובץ מקושר להערה",
            Purpose = "NoteLinkedFile",
            SelectionMode = FilePickerSelectionMode.Single,
            Tree = tree,
        }, cancellationToken).ConfigureAwait(true);

        if (picked is null || picked.Count == 0)
            return null;

        var p = picked[0];
        return new InspectionFilePickResult(
            p.FileName,
            string.IsNullOrWhiteSpace(p.Alternative) ? null : p.Alternative,
            Version: null,
            FullPath: null);
    }
}

internal sealed class V2InspectionReportEmailHost : IInspectionReportEmailHost
{
    public Task<bool> SendReportEmailAsync(int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class V2InspectionNoteScreenshotHost(
    GoogleAuthService authService,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    IInspectionReportService reportService,
    ISystemSettingsQueryService settings,
    ILoggerFactory? loggerFactory = null) : IInspectionNoteScreenshotHost
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly IInspectionReportService _reportService =
        reportService ?? throw new ArgumentNullException(nameof(reportService));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;

    public async Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId, CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            return InspectionScreenshotUploadResult.Fail("הערה לא חוקית.");

        // Clipboard must be read on the UI thread before any ConfigureAwait(false) hop.
        byte[] pngBytes;
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                return InspectionScreenshotUploadResult.Fail(
                    "אין תמונה בלוח (Clipboard). העתק/י צילום מסך תחילה.");
            }

            var src = System.Windows.Clipboard.GetImage();
            if (src is null)
                return InspectionScreenshotUploadResult.Fail("לא ניתן לקרוא את התמונה מהלוח.");

            using var ms = new System.IO.MemoryStream();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            return InspectionScreenshotUploadResult.Fail($"שגיאה בקריאת התמונה מהלוח: {ex.Message}");
        }

        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            hash = Convert.ToHexString(sha.ComputeHash(pngBytes));
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var note = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.NoteId == noteId)
            .Select(n => new { n.NoteId, n.ReportId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (note is null)
            return InspectionScreenshotUploadResult.Fail($"הערה {noteId} לא נמצאה.");

        try
        {
            var dup = await _reportService
                .CheckDuplicateNoteAttachmentAsync(note.ReportId, hash, cancellationToken)
                .ConfigureAwait(true);
            if (dup is not null)
            {
                if (dup.NoteId == noteId)
                {
                    return InspectionScreenshotUploadResult.Fail("התמונה הזו כבר צורפה להערה הזו.");
                }

                var confirm = System.Windows.MessageBox.Show(
                    "התמונה הזו כבר צורפה לסעיף אחר בדוח. האם אתה בטוח שברצונך לצרף אותה גם לסעיף הנוכחי?",
                    "צירוף צילום מסך — כפילות בדוח",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                {
                    return InspectionScreenshotUploadResult.Fail("העלאה בוטלה.");
                }
            }
        }
        catch
        {
            // Duplicate check is best-effort; continue upload if it fails.
        }

        try
        {
            var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
            var logger = _loggerFactory?.CreateLogger<GoogleNoteScreenshotUploadService>();
            var uploadService = new GoogleNoteScreenshotUploadService(_authService, _dbContextFactory, logger)
            {
                ReportsFolderId = dto.Inspection.InspectionReportsFolderId,
            };

            var fileName = $"note-{noteId}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var result = await uploadService
                .UploadScreenshotAsync(note.ReportId, noteId, fileName, pngBytes, "image/png", cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.GoogleDriveFileId))
            {
                return InspectionScreenshotUploadResult.Fail(
                    result.ErrorMessage ?? "העלאת צילום מסך נכשלה.");
            }

            var attachment = new SiNetSQL.Models.InspectionNoteAttachment
            {
                NoteId = noteId,
                AttachmentType = SiNetSQL.Models.InspectionNoteAttachmentType.Screenshot,
                FileName = result.FileName ?? fileName,
                GoogleDriveFileId = result.GoogleDriveFileId,
                GoogleDriveUrl = result.GoogleDriveUrl,
                ContentHashSha256 = hash,
                FileSizeBytes = pngBytes.LongLength,
                UploadedAt = DateTime.UtcNow,
            };

            await _reportService.AddNoteAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
            return InspectionScreenshotUploadResult.Ok(attachment.GoogleDriveUrl);
        }
        catch (Exception ex)
        {
            return InspectionScreenshotUploadResult.Fail($"שגיאה בצירוף צילום מסך: {ex.Message}");
        }
    }

    public async Task<InspectionScreenshotOpenResult> OpenLastAsync(
        long noteId, CancellationToken cancellationToken = default)
    {
        if (noteId <= 0)
            return InspectionScreenshotOpenResult.Fail("הערה לא חוקית.");

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var url = await db.InspectionNoteAttachments
                .AsNoTracking()
                .Where(a => a.NoteId == noteId)
                .OrderByDescending(a => a.UploadedAt)
                .Select(a => a.GoogleDriveUrl)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(url))
            {
                return InspectionScreenshotOpenResult.Fail(
                    "אין תמונה זמינה לפתיחה (חסר קישור Google Drive).");
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
            return InspectionScreenshotOpenResult.Ok("נפתחה התמונה האחרונה.");
        }
        catch (Exception ex)
        {
            return InspectionScreenshotOpenResult.Fail($"שגיאה בפתיחת התמונה: {ex.Message}");
        }
    }
}

/// <summary>Opens a note-linked file via the native ProjectWork hubs (IActiveFileQueryHub / IFileOpenHub).</summary>
internal sealed class V2InspectionNoteLinkedFileHost(
    IActiveFileQueryHub activeFiles,
    IFileOpenHub fileOpen) : IInspectionNoteLinkedFileHost
{
    private readonly IActiveFileQueryHub _activeFiles =
        activeFiles ?? throw new ArgumentNullException(nameof(activeFiles));
    private readonly IFileOpenHub _fileOpen =
        fileOpen ?? throw new ArgumentNullException(nameof(fileOpen));

    public async Task<InspectionLinkedFileOpenResult> OpenAsync(
        InspectionLinkedFileOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var note = new SiNetSQL.Models.InspectionNote
        {
            NoteId = request.NoteId,
            LinkedFileName = request.LinkedFileName,
            LinkedAlternative = request.LinkedAlternative,
            LinkedVersion = request.LinkedVersion,
        };
        var report = new SiNetSQL.Models.InspectionReport
        {
            ReportId = request.ReportId,
            ReviewedVersion = request.ReviewedVersion,
            ReviewedFiles = request.ReviewedFiles
                .Select(f => new SiNetSQL.Models.InspectionReportReviewedFile
                {
                    FileName = f.FileName,
                    Alternative = f.Alternative,
                    SortOrder = f.SortOrder,
                })
                .ToList(),
        };

        var available = _fileOpen.IsAvailable && _activeFiles.IsAvailable;
        var decision = InspectionFileLinkHelper.DecideOpen(note, report, fileOpenServiceAvailable: available);
        if (!decision.IsEnabled || string.IsNullOrWhiteSpace(decision.FileName))
        {
            return InspectionLinkedFileOpenResult.Fail(
                string.IsNullOrWhiteSpace(decision.Tooltip)
                    ? "אין קובץ מקושר לפתיחה."
                    : decision.Tooltip);
        }

        var match = _activeFiles.FindActiveFileByName(decision.FileName!);
        int? versionNumber = int.TryParse(decision.Version, out var parsed) ? parsed : null;

        var openRequest = new FileOpenRequest(
            FileId: match?.FileId,
            AlternativeName: string.IsNullOrWhiteSpace(decision.Alternative) ? null : decision.Alternative,
            VersionNumber: versionNumber);

        try
        {
            var result = await _fileOpen.OpenAsync(openRequest, cancellationToken).ConfigureAwait(false);

            var message = result.Outcome switch
            {
                FileOpenOutcome.OpenedInAcc => $"נפתח ב-ACC: {decision.FileName}",
                FileOpenOutcome.OpenedLocally => $"נפתח מקומית: {decision.FileName}",
                FileOpenOutcome.NotFound => $"הקובץ לא נמצא: {decision.FileName}",
                FileOpenOutcome.Unavailable =>
                    InspectionFileLinkHelper.MessageWorkWindowRequiredForOpen,
                FileOpenOutcome.Failed =>
                    $"שגיאה בפתיחת קובץ: {result.Error}",
                _ => decision.Tooltip ?? string.Empty,
            };

            return result.Success
                ? InspectionLinkedFileOpenResult.Ok(message)
                : InspectionLinkedFileOpenResult.Fail(message);
        }
        catch (Exception ex)
        {
            return InspectionLinkedFileOpenResult.Fail($"שגיאה בפתיחת קובץ: {ex.Message}");
        }
    }
}

/// <summary>Maps native ProjectWork active-file DTOs onto the legacy FileTreePicker shape.</summary>
internal static class NativeActiveFileTreeMapper
{
    public static IReadOnlyList<LegacyActiveFolderInfo> ToLegacy(IReadOnlyList<NativeActiveFolderInfo> folders)
        => folders.Select(MapFolder).ToList();

    private static LegacyActiveFolderInfo MapFolder(NativeActiveFolderInfo folder) =>
        new(
            folder.FolderId,
            folder.Title,
            folder.FullPath,
            folder.Files.Select(MapFile).ToList(),
            folder.Children.Select(MapFolder).ToList());

    private static LegacyActiveFileInfo MapFile(NativeActiveFileInfo file) =>
        new(
            file.FileId,
            file.FileName,
            file.Extension,
            file.ProjectNumber,
            file.FolderId,
            file.StorageDestination,
            file.Alternatives.Select(a => new LegacyActiveAlternativeInfo(
                a.AlternativeName,
                a.Versions.Select(v => new LegacyActiveVersionInfo(
                    v.VersionNumber,
                    v.Description,
                    v.FullPath,
                    v.Size,
                    v.Date,
                    v.AccItemId,
                    v.AccViewerUrl)).ToList())).ToList());
}

internal sealed class V2InspectionReportExportPort(
    GoogleAuthService authService,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ISystemSettingsQueryService settings,
    IInspectionReportService reportService,
    ILoggerFactory? loggerFactory = null) : IInspectionReportExportPort
{
    private readonly GoogleAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IInspectionReportService _reportService =
        reportService ?? throw new ArgumentNullException(nameof(reportService));
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;

    public async Task<InspectionExportResult> ExportAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportService
                .GetReportWithSeriesAsync(reportId, cancellationToken)
                .ConfigureAwait(false);
            if (report is null)
            {
                return InspectionExportResult.Fail($"דוח {reportId} לא נמצא.");
            }

            var templateId = ExtractSpreadsheetId(report.SourceFileUrn);
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return InspectionExportResult.Fail("לא נמצא מזהה תבנית בדוח.");
            }

            var export = await CreateExportServiceAsync(cancellationToken).ConfigureAwait(false);
            var result = await export
                .ExportReportAsync(reportId, templateId, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? InspectionExportResult.Ok(result.DestinationUrl)
                : InspectionExportResult.Fail(result.ErrorMessage ?? "ייצוא נכשל.");
        }
        catch (Exception ex)
        {
            return InspectionExportResult.Fail($"שגיאת ייצוא: {ex.Message}");
        }
    }

    public async Task<InspectionExportResult> ShareAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportService
                .GetReportWithSeriesAsync(reportId, cancellationToken)
                .ConfigureAwait(false);
            if (report is null)
            {
                return InspectionExportResult.Fail($"דוח {reportId} לא נמצא.");
            }

            var spreadsheetId = ExtractSpreadsheetId(report.SentSpreadsheetId)
                ?? ExtractSpreadsheetId(report.SentSpreadsheetUrl);
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                return InspectionExportResult.Fail("אין גיליון מיוצא לשיתוף — ייצא דוח קודם.");
            }

            var export = await CreateExportServiceAsync(cancellationToken).ConfigureAwait(false);
            var result = await export
                .ShareReportAnyoneWithLinkAsync(spreadsheetId, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? InspectionExportResult.Ok(result.SpreadsheetUrl)
                : InspectionExportResult.Fail(result.ErrorMessage ?? "שיתוף נכשל.");
        }
        catch (Exception ex)
        {
            return InspectionExportResult.Fail($"שגיאת שיתוף: {ex.Message}");
        }
    }

    public Task OpenTemplateAsync(int seriesId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private async Task<GoogleReportExportService> CreateExportServiceAsync(CancellationToken cancellationToken)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var logger = _loggerFactory?.CreateLogger<GoogleReportExportService>();
        return new GoogleReportExportService(_authService, _dbContextFactory, logger)
        {
            ReportsFolderId = dto.Inspection.InspectionReportsFolderId,
        };
    }

    private static string? ExtractSpreadsheetId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            return null;
        }

        if (!urlOrId.Contains('/'))
        {
            return urlOrId;
        }

        var match = Regex.Match(urlOrId, @"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : urlOrId;
    }
}
