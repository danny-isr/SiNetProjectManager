using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Inspection;

internal sealed class SqlInspectionWorkspace(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionWorkspace
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionSeries
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Modified)
            .Select(s => new InspectionSeriesSummary(
                s.SeriesId,
                s.SeriesName ?? $"סדרה {s.SeriesId}"))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
        int projectId, int seriesId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return [];
        }

        // seriesId <= 0 means reports not bound to an InspectionSeries (legacy / native empty create).
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // Avoid SQL COALESCE across InspectorName (Hebrew_100) and SIUser.Name (Hebrew_CI_AS).
        var query = db.InspectionReports.AsNoTracking().Where(r => r.ProjectId == projectId);
        query = seriesId > 0
            ? query.Where(r => r.SeriesId == seriesId)
            : query.Where(r => r.SeriesId == null);

        var raw = await query
            .OrderByDescending(r => r.ReportNumber)
            .Select(r => new
            {
                r.ReportId,
                r.ReportNumber,
                r.InspectionDate,
                r.InspectorName,
                InspectorUserName = r.Inspector != null ? r.Inspector.Name : null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return raw
            .Select(r => new InspectionReportRow(
                r.ReportId,
                r.ReportNumber,
                r.InspectionDate,
                r.InspectorName ?? r.InspectorUserName))
            .ToList();
    }

    public async Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var raw = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.ReportId == reportId)
            .OrderBy(n => n.Section.Chapter.ChapterNumber)
            .ThenBy(n => n.Section.SectionCode)
            .ThenBy(n => n.NoteSubIndex)
            .Select(n => new
            {
                n.NoteId,
                n.NoteSubIndex,
                n.NoteText,
                StatusLabel = n.NoteStatusLookup != null ? n.NoteStatusLookup.HebrewLabel : null,
                n.NoteStatus,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return raw
            .Select(n => new InspectionNoteRow(
                n.NoteId,
                n.NoteSubIndex,
                n.NoteText,
                n.StatusLabel ?? n.NoteStatus))
            .ToList();
    }

    public async Task<InspectionReportDetail?> GetReportDetailAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var raw = await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .Select(r => new
            {
                r.ReportId,
                r.ProjectId,
                r.SeriesId,
                r.ReportNumber,
                r.InspectionDate,
                r.InspectorName,
                InspectorUserName = r.Inspector != null ? r.Inspector.Name : null,
                r.ReviewedVersion,
                r.IsLockedAfterSend,
                r.SentAt,
                r.SentSpreadsheetUrl,
                r.SourceFileUrn,
                r.SourceFileVersion,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (raw is null)
        {
            return null;
        }

        return new InspectionReportDetail(
            raw.ReportId,
            raw.ProjectId,
            raw.SeriesId,
            raw.ReportNumber,
            raw.InspectionDate,
            raw.InspectorName ?? raw.InspectorUserName,
            raw.ReviewedVersion,
            raw.IsLockedAfterSend,
            raw.SentAt,
            raw.SentSpreadsheetUrl,
            raw.SourceFileUrn,
            raw.SourceFileVersion);
    }

    public async Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var notes = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.ReportId == reportId)
            .Select(n => new
            {
                n.NoteId,
                n.NoteSubIndex,
                n.NoteText,
                n.NoteStatus,
                n.NoteStatusId,
                n.PlannerResponseText,
                n.LinkedFileName,
                n.LinkedAlternative,
                n.LinkedVersion,
                AttachmentCount = n.Attachments.Count,
                LastAttachmentUrl = n.Attachments
                    .OrderByDescending(a => a.UploadedAt)
                    .Select(a => a.GoogleDriveUrl)
                    .FirstOrDefault(),
                SectionId = n.SectionId,
                SectionCode = n.Section.SectionCode,
                SectionTitle = n.Section.SectionName != null
                    ? n.Section.SectionName.Name
                    : null,
                SectionCodeFallback = n.Section.SectionCode,
                ChapterId = n.Section.Chapter.ChapterId,
                ChapterNumber = n.Section.Chapter.ChapterNumber,
                ChapterTitle = n.Section.Chapter.ChapterName != null
                    ? n.Section.Chapter.ChapterName.Name
                    : null,
                ChapterNumberFallback = n.Section.Chapter.ChapterNumber,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Numbered chapters only; sub-notes with 2+ dots (legacy parity).
        return notes
            .Where(n => n.ChapterNumber > 0 && InspectionQuestionnaireRules.IsNumberedSubNote(n.NoteSubIndex))
            .GroupBy(n => new
            {
                n.ChapterId,
                n.ChapterNumber,
                ChapterTitle = n.ChapterTitle ?? $"פרק {n.ChapterNumberFallback}",
            })
            .OrderBy(g => g.Key.ChapterNumber)
            .Select(chapterGroup => new InspectionChapterNode(
                chapterGroup.Key.ChapterId,
                chapterGroup.Key.ChapterNumber,
                chapterGroup.Key.ChapterTitle ?? string.Empty,
                chapterGroup
                    .GroupBy(n => new
                    {
                        n.SectionId,
                        n.SectionCode,
                        SectionTitle = n.SectionTitle ?? $"סעיף {n.SectionCodeFallback}",
                    })
                    .OrderBy(g => g.Key.SectionCode)
                    .Select(sectionGroup => new InspectionSectionNode(
                        sectionGroup.Key.SectionId,
                        sectionGroup.Key.SectionCode,
                        sectionGroup.Key.SectionTitle ?? string.Empty,
                        sectionGroup
                            .OrderBy(n => n.NoteSubIndex)
                            .Select(n => new InspectionNoteTreeRow(
                                n.NoteId,
                                n.NoteSubIndex,
                                n.NoteText,
                                n.NoteStatus,
                                n.NoteStatusId,
                                n.PlannerResponseText,
                                n.LinkedFileName,
                                n.LinkedAlternative,
                                n.LinkedVersion,
                                n.AttachmentCount,
                                n.LastAttachmentUrl))
                            .ToList()))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<InspectionGeneralFieldRow>> GetGeneralFieldsAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var raw = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.ReportId == reportId && n.Section.Chapter.ChapterNumber == 0)
            .OrderBy(n => n.Section.SectionCode)
            .ThenBy(n => n.NoteSubIndex)
            .Select(n => new
            {
                n.NoteId,
                n.SectionId,
                Label = n.Section.SectionName != null
                    ? n.Section.SectionName.Name
                    : $"סעיף {n.Section.SectionCode}",
                n.NoteText,
                n.NoteStatus,
                n.NoteSubIndex,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return raw
            .Where(n => InspectionQuestionnaireRules.IsGeneralBaseNote(n.NoteSubIndex))
            .Select(n => new InspectionGeneralFieldRow(
                n.NoteId,
                n.SectionId,
                n.Label ?? string.Empty,
                n.NoteText,
                string.Equals(n.NoteStatus, InspectionQuestionnaireRules.ManualStatus, StringComparison.Ordinal)))
            .ToList();
    }

    public async Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionReportDrawings
            .AsNoTracking()
            .Where(d => d.ReportId == reportId)
            .OrderBy(d => d.FileName)
            .Select(d => new InspectionDrawingRow(
                d.Id,
                d.FileName,
                d.SourceFilePath,
                d.FileType.ToString(),
                d.StampStatus.ToString(),
                d.StampedFilePath,
                d.StampedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionReportReviewedFiles
            .AsNoTracking()
            .Where(f => f.ReportId == reportId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new InspectionReviewedFileRow(f.Id, f.FileName, f.Alternative, f.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
