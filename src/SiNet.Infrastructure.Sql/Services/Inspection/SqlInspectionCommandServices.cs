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
        note.NoteStatus = string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim();

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return InspectionNoteCommandResult.Ok(noteId);
    }

    public async Task<InspectionNoteCommandResult> SaveNoteAsync(
        long noteId, string? text, int? statusId, string? statusText, CancellationToken cancellationToken = default)
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
        note.NoteStatusId = statusId;
        note.NoteStatus = string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim();

        // Single write keeps text and status consistent (no split-brain on partial failure).
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

        var section = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == sectionId)
            .Select(s => new
            {
                s.SectionId,
                s.SectionCode,
                ChapterNumber = s.Chapter.ChapterNumber,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (section is null)
        {
            return InspectionNoteCommandResult.Fail($"סעיף {sectionId} לא נמצא.");
        }

        // Legacy parity: Level-3 index "Chapter.Section.Ordinal" (e.g. 1.1.2).
        var prefix = $"{section.ChapterNumber}.{section.SectionCode}.";

        // Serialize the read-max + insert so two concurrent adds cannot compute the same NoteSubIndex
        // and violate IX_InspectionNotes_Report_Section_SubIndex. Serializable is relational-only
        // (the EF InMemory provider used by unit tests does not support transactions/isolation).
        await using var transaction = db.Database.IsRelational()
            ? await db.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false)
            : null;

        try
        {
            var existingSubNotes = await db.InspectionNotes
                .Where(n => n.ReportId == reportId && n.SectionId == sectionId)
                .Select(n => n.NoteSubIndex)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var subNoteCount = existingSubNotes.Count(idx =>
                idx != null && idx.StartsWith(prefix, StringComparison.Ordinal));
            var noteSubIndex = $"{prefix}{subNoteCount + 1}";

            var note = new InspectionNote
            {
                ReportId = reportId,
                SectionId = sectionId,
                NoteText = text,
                NoteSubIndex = noteSubIndex,
            };
            db.InspectionNotes.Add(note);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return InspectionNoteCommandResult.Ok(note.NoteId);
        }
        catch (DbUpdateException)
        {
            return InspectionNoteCommandResult.Fail(
                "הוספת ההערה נכשלה (התנגשות אינדקס). נסה שוב.");
        }
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

    public async Task<InspectionNoteCommandResult> RenumberNotesAsync(
        IReadOnlyList<(long NoteId, string SubIndex)> renumberings,
        CancellationToken cancellationToken = default)
    {
        if (renumberings.Count == 0)
            return InspectionNoteCommandResult.Ok();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var notes = new List<(InspectionNote Note, string FinalSubIndex)>();
        foreach (var (noteId, subIndex) in renumberings)
        {
            var note = await db.InspectionNotes.FindAsync([noteId], cancellationToken).ConfigureAwait(false);
            if (note is null)
                continue;

            if (await IsReportLockedAsync(db, note.ReportId, cancellationToken).ConfigureAwait(false))
                return InspectionNoteCommandResult.Fail("הדוח נעול לאחר שליחה.");

            notes.Add((note, subIndex));
        }

        if (notes.Count == 0)
            return InspectionNoteCommandResult.Ok();

        await using var tx = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // Phase 1: unique temp indices avoid IX_InspectionNotes_Report_Section_SubIndex collisions on swap.
            foreach (var (note, _) in notes)
                note.NoteSubIndex = $"~tmp.{note.NoteId}";

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Phase 2: final indices
            foreach (var (note, finalSubIndex) in notes)
                note.NoteSubIndex = finalSubIndex;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return InspectionNoteCommandResult.Fail(
                "עדכון סדר ההערות נכשל (התנגשות אינדקס). נסה שוב.");
        }

        return InspectionNoteCommandResult.Ok();
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

/// <summary>
/// SQL-backed report commands. Create requires a host Google/template adapter (V2);
/// this implementation returns a clear failure for create so unlock/delete still work standalone.
/// </summary>
public sealed class SqlInspectionReportCommandService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionReportCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

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
            return InspectionReportCommandResult.Fail("לא נבחר פרויקט.");

        // Standalone native create: persist report (+ optional section snapshot).
        // Full Google Sheets template scan/sync remains on the V2 host adapter
        // (V2InspectionReportCommandService). An empty-section report is still a valid
        // work target for PerformProfessionalReview completion.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            int nextNumber;
            if (seriesId is > 0)
            {
                nextNumber = await db.InspectionReports
                    .Where(r => r.SeriesId == seriesId.Value)
                    .Select(r => (int?)r.ReportNumber)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) ?? 0;
            }
            else
            {
                nextNumber = await db.InspectionReports
                    .Where(r => r.ProjectId == projectId && r.SeriesId == null)
                    .Select(r => (int?)r.ReportNumber)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) ?? 0;
            }

            nextNumber++;

            var report = new InspectionReport
            {
                ProjectId = projectId,
                SeriesId = seriesId is > 0 ? seriesId : null,
                ReportNumber = nextNumber,
                InspectionDate = DateTime.UtcNow,
                InspectorName = inspectorName,
                InspectorId = inspectorId,
                SourceFileUrn = string.IsNullOrWhiteSpace(templateUrl) ? spreadsheetId : templateUrl,
                SourceFileVersion = nextNumber.ToString(),
            };

            db.InspectionReports.Add(report);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var sectionQuery = db.Sections
                .AsNoTracking()
                .Include(s => s.Chapter)
                .Where(s => s.IsActive);

            if (seriesId is > 0)
                sectionQuery = sectionQuery.Where(s => s.Chapter.SeriesId == seriesId.Value);

            var activeSections = await sectionQuery
                .OrderBy(s => s.Chapter.ChapterNumber)
                .ThenBy(s => s.SectionCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var section in activeSections)
            {
                var noteSubIndex = section.Chapter.ChapterNumber == 0
                    ? section.SectionCode.ToString()
                    : $"{section.FullCode}.1";

                db.InspectionNotes.Add(new InspectionNote
                {
                    ReportId = report.ReportId,
                    SectionId = section.SectionId,
                    NoteSubIndex = noteSubIndex,
                });
            }

            if (activeSections.Count > 0)
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return InspectionReportCommandResult.Ok(report.ReportId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return InspectionReportCommandResult.Fail($"שגיאה ביצירת דוח: {ex.Message}");
        }
    }

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
