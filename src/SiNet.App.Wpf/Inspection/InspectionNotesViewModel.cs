namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Placeholder view model for the Inspection notes area. Note editing, ordering, and status flow
/// (reusing the extracted <c>InspectionNoteHelpers</c> / <c>InspectionNoteOrdering</c>) will be
/// added when this area is migrated; the foundation only provides a header + placeholder.
/// </summary>
public sealed class InspectionNotesViewModel : ObservableObject
{
    public string Title => "Notes";

    public string Placeholder { get; } = "Inspection notes — to be migrated.";
}
