using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;

/// <summary>Native New System host for JobType ↔ Workflow mapping admin («מדיניות סוג↔תהליך»).</summary>
public sealed class ProjectTypeWorkflowPolicyWindow : Window
{
    public ProjectTypeWorkflowPolicyWindow(ProjectTypeWorkflowPolicyViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = "מדיניות סוג↔תהליך";
        Width = 1100;
        Height = 700;
        MinWidth = 860;
        MinHeight = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new ProjectTypeWorkflowPolicyView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
