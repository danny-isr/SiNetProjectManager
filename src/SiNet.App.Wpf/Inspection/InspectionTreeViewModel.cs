using System.Collections.ObjectModel;
using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// View model for the Inspection project/series tree area. First migrated capability: it loads the
/// inspection series for a project through the clean <see cref="IInspectionWorkspace"/> port, so the
/// new shell shows real (or adapter-provided) series without depending on the legacy stack. Report
/// rows, selection state, notes/drawings/reviewed-plan stay in the legacy window for later phases.
/// </summary>
public sealed class InspectionTreeViewModel : ObservableObject
{
    private readonly IInspectionWorkspace _workspace;
    private bool _isLoading;

    public InspectionTreeViewModel(IInspectionWorkspace workspace)
    {
        _workspace = workspace;
    }

    public string Title => "Tree";

    public ObservableCollection<InspectionSeriesSummary> Series { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>Loads series for a project. Empty when no source is bound (early-migration default).</summary>
    public async Task LoadSeriesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var series = await _workspace.GetSeriesAsync(projectId, cancellationToken).ConfigureAwait(true);
            Series.Clear();
            foreach (var s in series)
            {
                Series.Add(s);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
