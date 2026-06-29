using SiNet.Application.Abstractions.Inspection;

namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Strangler adapter that implements the new <see cref="IInspectionWorkspace"/> Application port by
/// delegating to the legacy-host <see cref="ILegacyInspectionSource"/> seam. It maps the bridge-local
/// <see cref="LegacyInspectionSeriesDto"/> onto the UI-agnostic <see cref="InspectionSeriesSummary"/>.
/// <para>
/// The seam is optional: when no host binds it (e.g. the new <c>SiNet.App.Wpf</c> shell during early
/// migration), the workspace returns an empty list so the rebuilt Inspection screen composes and
/// renders without coupling the new app to <c>SiNetSQL</c>. The legacy WPF host supplies a real
/// source and the new tree shows live series. Replace this with a native infrastructure
/// implementation once Inspection is fully migrated.
/// </para>
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

        var result = new List<InspectionSeriesSummary>(series.Count);
        foreach (var s in series)
        {
            result.Add(new InspectionSeriesSummary(s.SeriesId, s.DisplayName));
        }

        return result;
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

        var result = new List<InspectionReportRow>(reports.Count);
        foreach (var r in reports)
        {
            result.Add(new InspectionReportRow(r.ReportId, r.ReportNumber, r.InspectionDate, r.InspectorName));
        }

        return result;
    }
}
