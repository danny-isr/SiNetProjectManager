using System.Windows;
using SiNet.App.Wpf.WorkSurfaces;

namespace SiNetProjectManagerV2.Services;

/// <summary>V2 adapter: closes MainWindow's floating ProjectWork task window.</summary>
internal sealed class MainWindowTaskFamilyWindowGate : ITaskFamilyWindowGate
{
    public void CloseProjectWorkTaskWindows()
    {
        if (Application.Current?.MainWindow is MainWindow main)
            main.CloseProjectWorkTaskWindow();
    }
}
