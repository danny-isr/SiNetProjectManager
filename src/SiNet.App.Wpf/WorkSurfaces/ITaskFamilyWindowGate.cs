namespace SiNet.App.Wpf.WorkSurfaces;

/// <summary>
/// Legacy seam: close ProjectWork floating task windows.
/// Prefer <see cref="ITaskSurfaceWindowCoordinator"/> (SOF-009) for all task surfaces.
/// </summary>
public interface ITaskFamilyWindowGate
{
    void CloseProjectWorkTaskWindows();
}
