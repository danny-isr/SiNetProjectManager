using System.Windows;

namespace SiNetProjectManagerV2;

/// <summary>
/// Help Center window for the Workflow Visual Designer.
/// Displays a comprehensive Hebrew guide covering nodes, connectors,
/// triggers, conditions, actions, start triggers, sub-workflows,
/// and keyboard shortcuts.
/// </summary>
public partial class WorkflowDesignerHelpWindow : Window
{
    public WorkflowDesignerHelpWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
