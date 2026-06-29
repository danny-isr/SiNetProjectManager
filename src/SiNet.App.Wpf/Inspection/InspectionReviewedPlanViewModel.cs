namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Placeholder view model for the Inspection reviewed-plan area. Reviewed-plan candidate/row
/// shaping will reuse the extracted <c>InspectionReviewedPlanBuilder</c> via the future workspace
/// port; the foundation only provides a header + placeholder.
/// </summary>
public sealed class InspectionReviewedPlanViewModel : ObservableObject
{
    public string Title => "Reviewed Plan";

    public string Placeholder { get; } = "Reviewed plan — to be migrated.";
}
