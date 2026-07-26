using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>Closes the shared ProjectWork floating task window (Legacy or NewShell).</summary>
internal sealed class MainWindowTaskFamilyWindowGate(ProjectWorkTaskFloatingHost taskFloatingHost) : ITaskFamilyWindowGate
{
    private readonly ProjectWorkTaskFloatingHost _taskFloatingHost =
        taskFloatingHost ?? throw new ArgumentNullException(nameof(taskFloatingHost));

    public void CloseProjectWorkTaskWindows() => _taskFloatingHost.CloseIfOpen();
}
