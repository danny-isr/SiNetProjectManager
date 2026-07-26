namespace SiNet.App.Wpf.WorkSurfaces;

/// <summary>
/// Host seam: close floating task-family windows when switching to another family
/// (e.g. ProjectWork ↔ Inspection) so only one execution window stays open.
/// </summary>
public interface ITaskFamilyWindowGate
{
    void CloseProjectWorkTaskWindows();
}
