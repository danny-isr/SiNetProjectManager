namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Opens the Task Workbench window (legacy interface name retained for shell registration).
/// </summary>
public interface ITaskPanelReadOnlyWindowFactory
{
    /// <summary>Creates a new workbench window (tests / callers that manage lifetime themselves).</summary>
    TaskWorkbenchView Create();

    /// <summary>
    /// Shows the process-wide workbench singleton, or activates it if already open.
    /// </summary>
    TaskWorkbenchView ShowOrActivate();
}
