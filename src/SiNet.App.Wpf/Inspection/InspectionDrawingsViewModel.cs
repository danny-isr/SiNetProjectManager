namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Placeholder view model for the Inspection drawings area. Drawing discovery/stamping will reuse
/// the extracted <c>IInspectionDrawingManagementService</c> + <c>InspectionDrawingStampBuilder</c>
/// via the future Inspection workspace port; the foundation only provides a header + placeholder.
/// </summary>
public sealed class InspectionDrawingsViewModel : ObservableObject
{
    public string Title => "Drawings";

    public string Placeholder { get; } = "Drawing management — to be migrated.";
}
