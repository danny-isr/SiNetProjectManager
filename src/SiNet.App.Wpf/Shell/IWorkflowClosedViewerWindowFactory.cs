using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Opens the closed-world workflow definition viewer (read-only / dry-run, no save).
/// Host (V2) supplies the concrete window so App.Wpf stays free of SiNetSQL / V2 references.
/// </summary>
public interface IWorkflowClosedViewerWindowFactory
{
    Window Create();
}
