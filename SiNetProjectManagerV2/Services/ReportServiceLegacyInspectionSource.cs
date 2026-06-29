using System.Globalization;
using SiNet.LegacyBridge.Inspection;
using SiNetSQL.Services.InspectionSync;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Binds the new <see cref="ILegacyInspectionSource"/> strangler seam to the existing legacy
/// <see cref="IInspectionReportService"/>.
/// <para>
/// This is the single place that knows both worlds: it calls the legacy series read and projects the
/// rich EF <c>InspectionSeries</c> entity down to the bridge-local <see cref="LegacyInspectionSeriesDto"/>
/// (no presentation members cross the boundary), reusing the same display-name fallback the legacy
/// Inspection window uses. It swallows failures into an empty result so the new screen degrades
/// gracefully when no data is available.
/// </para>
/// </summary>
internal sealed class ReportServiceLegacyInspectionSource : ILegacyInspectionSource
{
    private readonly IInspectionReportService _reportService;

    public ReportServiceLegacyInspectionSource(IInspectionReportService reportService)
    {
        _reportService = reportService;
    }

    public async Task<IReadOnlyList<LegacyInspectionSeriesDto>> GetSeriesForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dbSeries = await _reportService
                .GetSeriesForProjectAsync(projectId, cancellationToken)
                .ConfigureAwait(false);

            var result = new List<LegacyInspectionSeriesDto>(dbSeries.Count);
            foreach (var s in dbSeries)
            {
                var name = string.IsNullOrWhiteSpace(s.SeriesName)
                    ? $"סדרה #{s.SeriesId} ({s.Created.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture)})"
                    : s.SeriesName;
                result.Add(new LegacyInspectionSeriesDto(s.SeriesId, name));
            }

            return result;
        }
        catch
        {
            return [];
        }
    }
}
