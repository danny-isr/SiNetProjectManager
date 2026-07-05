namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Opens the Task Workbench window (legacy interface name retained for shell registration).
/// </summary>
public interface ITaskPanelReadOnlyWindowFactory
{
    TaskWorkbenchView Create();
}
