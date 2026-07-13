using SiNet.Application.Abstractions.Inspection;

namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Strangler adapter kept for hosts that still bind <see cref="ILegacyInspectionSource"/>.
/// Prefer <c>SqlInspectionWorkspace</c> via <c>AddSiNetInspectionSql</c>.
/// </summary>
internal sealed class LegacyInspectionWorkspace : IInspectionWorkspace
{
    private readonly ILegacyInspectionSource? _source;

    public LegacyInspectionWorkspace(ILegacyInspectionSource? source = null)
    {
        _source = source;
    }

    public async Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        if (_source is null)
        {
            return [];
        }

        var series = await _source
            .GetSeriesForProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        return series.Select(s => new InspectionSeriesSummary(s.SeriesId, s.DisplayName)).ToList();
    }

    public async Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
        int projectId, int seriesId, CancellationToken cancellationToken = default)
    {
        if (_source is null)
        {
            return [];
        }

        var reports = await _source
            .GetReportsForSeriesAsync(projectId, seriesId, cancellationToken)
            .ConfigureAwait(false);

        return reports
            .Select(r => new InspectionReportRow(r.ReportId, r.ReportNumber, r.InspectionDate, r.InspectorName))
            .ToList();
    }

    public async Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
        int reportId, CancellationToken cancellationToken = default)
    {
        if (_source is null)
        {
            return [];
        }

        var notes = await _source
            .GetNotesForReportAsync(reportId, cancellationToken)
            .ConfigureAwait(false);

        return notes.Select(n => new InspectionNoteRow(n.NoteId, n.Number, n.Text, n.Status)).ToList();
    }

    public Task<InspectionReportDetail?> GetReportDetailAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionReportDetail?>(null);

    public Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionChapterNode>>([]);

    public Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionDrawingRow>>([]);

    public Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionReviewedFileRow>>([]);
}
