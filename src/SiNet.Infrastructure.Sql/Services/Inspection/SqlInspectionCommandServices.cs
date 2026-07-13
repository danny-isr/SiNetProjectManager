using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Inspection;

internal sealed class SqlInspectionNoteCommandService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionNoteCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<InspectionNoteCommandResult> SaveNoteTextAsync(
        long noteId, string? text, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var note = await db.InspectionNotes.FindAsync([noteId], cancellationToken).ConfigureAwait(false);
        if (note is null)
        {
            return InspectionNoteCommandResult.Fail($"הערה {noteId} לא נמצאה.");
        }

        if (await IsReportLockedAsync(db, note.ReportId, cancellationToken).ConfigureAwait(false))
        {
            return InspectionNoteCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        note.NoteText = text;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionNoteCommandResult.Ok(noteId);
    }

    public async Task<InspectionNoteCommandResult> SaveNoteStatusAsync(
        long noteId, int? statusId, string? statusText, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var note = await db.InspectionNotes.FindAsync([noteId], cancellationToken).ConfigureAwait(false);
        if (note is null)
        {
            return InspectionNoteCommandResult.Fail($"הערה {noteId} לא נמצאה.");
        }

        if (await IsReportLockedAsync(db, note.ReportId, cancellationToken).ConfigureAwait(false))
        {
            return InspectionNoteCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        note.NoteStatusId = statusId;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            note.NoteStatus = statusText.Trim();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionNoteCommandResult.Ok(noteId);
    }

    public async Task<InspectionNoteCommandResult> AddNoteAsync(
        int reportId, int sectionId, string? text, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0 || sectionId <= 0)
        {
            return InspectionNoteCommandResult.Fail("דוח או סעיף לא תקפים.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await IsReportLockedAsync(db, reportId, cancellationToken).ConfigureAwait(false))
        {
            return InspectionNoteCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        var sectionExists = await db.Sections.AnyAsync(s => s.SectionId == sectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!sectionExists)
        {
            return InspectionNoteCommandResult.Fail($"סעיף {sectionId} לא נמצא.");
        }

        var existingCount = await db.InspectionNotes
            .CountAsync(n => n.ReportId == reportId && n.SectionId == sectionId, cancellationToken)
            .ConfigureAwait(false);

        var note = new InspectionNote
        {
            ReportId = reportId,
            SectionId = sectionId,
            NoteText = text,
            NoteSubIndex = (existingCount + 1).ToString(),
        };
        db.InspectionNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionNoteCommandResult.Ok(note.NoteId);
    }

    public async Task<InspectionNoteCommandResult> SetNoteLinkedFileAsync(
        long noteId,
        string? fileName,
        string? alternative,
        string? version,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var note = await db.InspectionNotes.FindAsync([noteId], cancellationToken).ConfigureAwait(false);
        if (note is null)
        {
            return InspectionNoteCommandResult.Fail($"הערה {noteId} לא נמצאה.");
        }

        if (await IsReportLockedAsync(db, note.ReportId, cancellationToken).ConfigureAwait(false))
        {
            return InspectionNoteCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        note.LinkedFileName = fileName;
        note.LinkedAlternative = alternative;
        note.LinkedVersion = version;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionNoteCommandResult.Ok(noteId);
    }

    private static async Task<bool> IsReportLockedAsync(
        SiNetSQLDbContext db, int reportId, CancellationToken cancellationToken) =>
        await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .Select(r => r.IsLockedAfterSend)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}

internal sealed class SqlInspectionReportCommandService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionReportCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<InspectionReportCommandResult> UnlockReportAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports.FindAsync([reportId], cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return InspectionReportCommandResult.Fail($"דוח {reportId} לא נמצא.");
        }

        report.IsLockedAfterSend = false;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionReportCommandResult.Ok(reportId);
    }

    public async Task<InspectionReportCommandResult> DeleteReportAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports
            .Include(r => r.InspectionNotes)
            .Include(r => r.Drawings)
            .Include(r => r.ReviewedFiles)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);
        if (report is null)
        {
            return InspectionReportCommandResult.Fail($"דוח {reportId} לא נמצא.");
        }

        db.InspectionNotes.RemoveRange(report.InspectionNotes);
        db.InspectionReportDrawings.RemoveRange(report.Drawings);
        db.InspectionReportReviewedFiles.RemoveRange(report.ReviewedFiles);
        db.InspectionReports.Remove(report);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionReportCommandResult.Ok(reportId);
    }

    public async Task<InspectionReportCommandResult> SetReviewedVersionAsync(
        int reportId, string? reviewedVersion, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports.FindAsync([reportId], cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return InspectionReportCommandResult.Fail($"דוח {reportId} לא נמצא.");
        }

        if (report.IsLockedAfterSend)
        {
            return InspectionReportCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        report.ReviewedVersion = reviewedVersion;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionReportCommandResult.Ok(reportId);
    }

    public async Task<InspectionReportCommandResult> ReplaceReviewedFilesAsync(
        int reportId,
        IReadOnlyList<InspectionReviewedFileRow> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports
            .Include(r => r.ReviewedFiles)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);
        if (report is null)
        {
            return InspectionReportCommandResult.Fail($"דוח {reportId} לא נמצא.");
        }

        if (report.IsLockedAfterSend)
        {
            return InspectionReportCommandResult.Fail("הדוח נעול לאחר שליחה.");
        }

        db.InspectionReportReviewedFiles.RemoveRange(report.ReviewedFiles);
        var order = 0;
        foreach (var file in files)
        {
            db.InspectionReportReviewedFiles.Add(new InspectionReportReviewedFile
            {
                ReportId = reportId,
                FileName = file.FileName,
                Alternative = file.Alternative,
                SortOrder = order++,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionReportCommandResult.Ok(reportId);
    }
}

internal sealed class SqlInspectionDrawingCommandService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionDrawingCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<InspectionDrawingCommandResult> AddDrawingAsync(
        int reportId,
        string sourceFilePath,
        string fileName,
        string fileType,
        CancellationToken cancellationToken = default)
    {
        if (reportId <= 0 || string.IsNullOrWhiteSpace(sourceFilePath) || string.IsNullOrWhiteSpace(fileName))
        {
            return InspectionDrawingCommandResult.Fail("נתוני שרטוט לא תקפים.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var reportExists = await db.InspectionReports.AnyAsync(r => r.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);
        if (!reportExists)
        {
            return InspectionDrawingCommandResult.Fail($"דוח {reportId} לא נמצא.");
        }

        var type = Enum.TryParse<DrawingFileType>(fileType, ignoreCase: true, out var parsed)
            ? parsed
            : sourceFilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? DrawingFileType.Pdf
                : DrawingFileType.Dwf;

        var drawing = new InspectionReportDrawing
        {
            ReportId = reportId,
            SourceFilePath = sourceFilePath.Trim(),
            FileName = fileName.Trim(),
            FileType = type,
        };
        db.InspectionReportDrawings.Add(drawing);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionDrawingCommandResult.Ok(drawing.Id);
    }

    public async Task<InspectionDrawingCommandResult> RemoveDrawingAsync(
        int drawingId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var drawing = await db.InspectionReportDrawings.FindAsync([drawingId], cancellationToken)
            .ConfigureAwait(false);
        if (drawing is null)
        {
            return InspectionDrawingCommandResult.Fail($"שרטוט {drawingId} לא נמצא.");
        }

        db.InspectionReportDrawings.Remove(drawing);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionDrawingCommandResult.Ok(drawingId);
    }
}

/// <summary>Placeholder until a host binds a Google Sheets export adapter.</summary>
internal sealed class UnavailableInspectionReportExportPort : IInspectionReportExportPort
{
    public Task<InspectionExportResult> ExportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionExportResult.NotAvailable());

    public Task<InspectionExportResult> ShareAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionExportResult.NotAvailable());

    public Task OpenTemplateAsync(int seriesId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
