using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// DEPRECATED (SOF-009): superseded by <see cref="ITaskSurfaceWindowCoordinator"/> registered in
/// <c>AddSiNetWorkSurfaces</c>. Kept inactive (not DI-registered) pending later deletion after soak.
/// </summary>
internal sealed class MainWindowTaskFamilyWindowGate(ProjectWorkTaskFloatingHost taskFloatingHost) : ITaskFamilyWindowGate
{
    private readonly ProjectWorkTaskFloatingHost _taskFloatingHost =
        taskFloatingHost ?? throw new ArgumentNullException(nameof(taskFloatingHost));

    public void CloseProjectWorkTaskWindows() => _taskFloatingHost.CloseIfOpen();
}
