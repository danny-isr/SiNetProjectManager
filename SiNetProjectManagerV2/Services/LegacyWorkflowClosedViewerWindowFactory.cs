using System.Windows;
using SiNet.App.Wpf.Shell;
using SiNetProjectManagerV2.Dialogs;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: opens <see cref="WorkflowManagementWindow"/> whose first tab is the
/// closed-world viewer (view-only).
/// </summary>
internal sealed class LegacyWorkflowClosedViewerWindowFactory : IWorkflowClosedViewerWindowFactory
{
    public Window Create() => new WorkflowManagementWindow();
}
