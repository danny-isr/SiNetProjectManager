using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
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
        if (projectId <= 0 || seriesId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.SeriesId == seriesId)
            .OrderByDescending(r => r.ReportNumber)
            .Select(r => new InspectionReportRow(
                r.ReportId,
                r.ReportNumber,
                r.InspectionDate,
                r.InspectorName ?? (r.Inspector != null ? r.Inspector.Name : null)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.ReportId == reportId)
            .OrderBy(n => n.Section.Chapter.ChapterNumber)
            .ThenBy(n => n.Section.SectionCode)
            .ThenBy(n => n.NoteSubIndex)
            .Select(n => new InspectionNoteRow(
                n.NoteId,
                n.NoteSubIndex,
                n.NoteText,
                n.NoteStatusLookup != null ? n.NoteStatusLookup.HebrewLabel : n.NoteStatus))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InspectionReportDetail?> GetReportDetailAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .Select(r => new InspectionReportDetail(
                r.ReportId,
                r.ProjectId,
                r.SeriesId,
                r.ReportNumber,
                r.InspectionDate,
                r.InspectorName ?? (r.Inspector != null ? r.Inspector.Name : null),
                r.ReviewedVersion,
                r.IsLockedAfterSend,
                r.SentAt,
                r.SentSpreadsheetUrl,
                r.SourceFileUrn,
                r.SourceFileVersion))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
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
                Status = n.NoteStatusLookup != null ? n.NoteStatusLookup.HebrewLabel : n.NoteStatus,
                n.NoteStatusId,
                n.PlannerResponseText,
                n.LinkedFileName,
                n.LinkedAlternative,
                n.LinkedVersion,
                AttachmentCount = n.Attachments.Count,
                SectionId = n.SectionId,
                SectionCode = n.Section.SectionCode,
                SectionTitle = n.Section.SectionName != null
                    ? n.Section.SectionName.Name
                    : $"סעיף {n.Section.SectionCode}",
                ChapterId = n.Section.Chapter.ChapterId,
                ChapterNumber = n.Section.Chapter.ChapterNumber,
                ChapterTitle = n.Section.Chapter.ChapterName != null
                    ? n.Section.Chapter.ChapterName.Name
                    : $"פרק {n.Section.Chapter.ChapterNumber}",
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notes
            .GroupBy(n => new { n.ChapterId, n.ChapterNumber, n.ChapterTitle })
            .OrderBy(g => g.Key.ChapterNumber)
            .Select(chapterGroup => new InspectionChapterNode(
                chapterGroup.Key.ChapterId,
                chapterGroup.Key.ChapterNumber,
                chapterGroup.Key.ChapterTitle ?? string.Empty,
                chapterGroup
                    .GroupBy(n => new { n.SectionId, n.SectionCode, n.SectionTitle })
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
                                n.Status,
                                n.NoteStatusId,
                                n.PlannerResponseText,
                                n.LinkedFileName,
                                n.LinkedAlternative,
                                n.LinkedVersion,
                                n.AttachmentCount))
                            .ToList()))
                    .ToList()))
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
