namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Root view model for the rebuilt Inspection screen. It composes the five sub-area view models
/// (tree, notes, drawings, reviewed plan, report) so the screen can be developed and migrated one
/// area at a time. This is the new target UI foundation — it does NOT replace the legacy
/// <c>FloatingInspectionViewModel</c> window yet. It coordinates the read-only flow: when the tree's
/// selected report changes, the notes area reloads. Sub-areas are injected so each can evolve
/// independently and be unit-tested in isolation.
/// </summary>
public sealed class InspectionShellViewModel : ObservableObject
{
    public InspectionShellViewModel(
        InspectionTreeViewModel tree,
        InspectionNotesViewModel notes,
        InspectionDrawingsViewModel drawings,
        InspectionReviewedPlanViewModel reviewedPlan,
        InspectionReportViewModel report)
    {
        Tree = tree;
        Notes = notes;
        Drawings = drawings;
        ReviewedPlan = reviewedPlan;
        Report = report;

        Tree.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(InspectionTreeViewModel.SelectedReport))
            {
                await Notes.LoadNotesAsync(Tree.SelectedReport?.ReportId).ConfigureAwait(true);
            }
        };
    }

    public string Title => "Inspection (new screen foundation)";

    public InspectionTreeViewModel Tree { get; }

    public InspectionNotesViewModel Notes { get; }

    public InspectionDrawingsViewModel Drawings { get; }

    public InspectionReviewedPlanViewModel ReviewedPlan { get; }

    public InspectionReportViewModel Report { get; }
}
