using System.Collections.ObjectModel;
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

    /// <summary>Loads series for a project. Empty when no source is bound (early-migration default).</summary>
    public async Task LoadSeriesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        _projectId = projectId;
        IsLoading = true;
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
        if (_selectedSeries is not { } series || _projectId <= 0)
        {
            return;
        }

        var rows = await _workspace
            .GetReportsAsync(_projectId, series.SeriesId, cancellationToken)
            .ConfigureAwait(true);
        foreach (var row in rows)
        {
            Reports.Add(row);
        }

        SelectedReport = Reports.Count > 0 ? Reports[0] : null;
    }
}
