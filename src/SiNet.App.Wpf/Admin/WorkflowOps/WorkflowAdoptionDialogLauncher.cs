using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public interface IWorkflowAdoptionDialogLauncher
{
    void Show(int projectId, Window? owner);
}

public sealed class WorkflowAdoptionDialogLauncher(IServiceProvider services) : IWorkflowAdoptionDialogLauncher
{
    public void Show(int projectId, Window? owner)
    {
        var window = services.GetRequiredService<AdoptExistingWorkflowWindow>();
        window.SetProject(projectId);
        window.Owner = owner;
        window.ShowDialog();
    }
}
