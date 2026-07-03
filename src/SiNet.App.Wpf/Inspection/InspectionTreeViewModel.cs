using System.Collections.ObjectModel;
using SiNet.App.Wpf.Infrastructure;
using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// View model for the Inspection project/series tree area. It loads inspection series for a project
/// through the clean <see cref="IInspectionWorkspace"/> port and, when a series is selected, loads its
/// read-only report rows. Selection and rows are read-only — notes/drawings/reviewed-plan and all
/// write/generate/sent-locked behaviour stay in the legacy window for later phases.
/// </summary>
public sealed class InspectionTreeViewModel : ObservableObject
{
    private readonly IInspectionWorkspace _workspace;
    private bool _isLoading;
    private int _projectId;
    private InspectionSeriesSummary? _selectedSeries;
    private InspectionReportRow? _selectedReport;
    private string? _errorMessage;

    public InspectionTreeViewModel(IInspectionWorkspace workspace)
    {
        _workspace = workspace;
    }

    public string Title => "Tree";

    public ObservableCollection<InspectionSeriesSummary> Series { get; } = [];

    /// <summary>Read-only report rows under <see cref="SelectedSeries"/>. No editing/generation.</summary>
    public ObservableCollection<InspectionReportRow> Reports { get; } = [];

    public InspectionSeriesSummary? SelectedSeries
    {
        get => _selectedSeries;
        set
        {
            if (SetField(ref _selectedSeries, value))
            {
                _ = LoadReportsAsync(CancellationToken.None);
            }
        }
    }

    public InspectionReportRow? SelectedReport
    {
        get => _selectedReport;
        set => SetField(ref _selectedReport, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>Loads series for a project. Empty when no source is bound (early-migration default).</summary>
    public async Task LoadSeriesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        _projectId = projectId;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var series = await _workspace.GetSeriesAsync(projectId, cancellationToken).ConfigureAwait(true);
            Series.Clear();
            Reports.Clear();
            foreach (var s in series)
            {
                Series.Add(s);
            }

            SelectedSeries = Series.Count > 0 ? Series[0] : null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(LoadSeriesAsync));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Loads the read-only report rows for the selected series. Empty when none selected.</summary>
    public async Task LoadReportsAsync(CancellationToken cancellationToken = default)
    {
        Reports.Clear();
        SelectedReport = null;
        ErrorMessage = null;
        if (_selectedSeries is not { } series || _projectId <= 0)
        {
            return;
        }

        try
        {
            var rows = await _workspace
                .GetReportsAsync(_projectId, series.SeriesId, cancellationToken)
                .ConfigureAwait(true);
            foreach (var row in rows)
            {
                Reports.Add(row);
            }

            SelectedReport = Reports.Count > 0 ? Reports[0] : null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(LoadReportsAsync));
        }
    }

    /// <summary>
    /// Task-mode selection: opens the <em>exact</em> report identified by
    /// <paramref name="reportId"/> for <paramref name="projectId"/>, with NO fallback to the first
    /// report. Scans the project's series for the one that contains the target report, selects that
    /// series, loads its rows, then selects the matching row. Returns <see langword="true"/> only
    /// when the exact target was found and selected; otherwise leaves no report selected and returns
    /// <see langword="false"/> so the caller can show a clear "target missing" error.
    /// </summary>
    public async Task<bool> SelectReportByIdAsync(
        int projectId, int reportId, CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        try
        {
            await LoadSeriesAsync(projectId, cancellationToken).ConfigureAwait(true);

            foreach (var series in Series)
            {
                var rows = await _workspace
                    .GetReportsAsync(projectId, series.SeriesId, cancellationToken)
                    .ConfigureAwait(true);

                if (!rows.Any(r => r.ReportId == reportId))
                {
                    continue;
                }

                _selectedSeries = series;
                OnPropertyChanged(nameof(SelectedSeries));

                Reports.Clear();
                foreach (var row in rows)
                {
                    Reports.Add(row);
                }

                var target = Reports.FirstOrDefault(r => r.ReportId == reportId);
                if (target.ReportId == reportId)
                {
                    SelectedReport = target;
                    return true;
                }
            }

            SelectedReport = null;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppErrorReporter.Report(ex, nameof(SelectReportByIdAsync));
            SelectedReport = null;
            return false;
        }
    }
}
