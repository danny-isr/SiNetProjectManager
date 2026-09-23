using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public interface IWorkflowAdoptionDialogLauncher
{
    void Show(int projectId, Window? owner);
}

public sealed class WorkflowAdoptionDialogLauncher(IServiceProvider services) : IWorkflowAdoptionDialogLauncher
{
    public void Show(int projectId, Window? owner)
    {
        var logger = services.GetService<IAppLogger>();
        AdoptionDebugLog.Write(
            "WorkflowAdoptionDialogLauncher.Show",
            $"ENTER projectId={projectId} ownerNull={owner is null} ownerType={owner?.GetType().FullName} owner.IsVisible={owner?.IsVisible} owner.IsActive={owner?.IsActive}",
            logger);
        try
        {
            Probe<IWorkflowAdoptionService>("IWorkflowAdoptionService", logger);
            Probe<IWorkflowAdoptionDialogLauncher>("IWorkflowAdoptionDialogLauncher", logger);
            Probe<AdoptExistingWorkflowViewModel>("AdoptExistingWorkflowViewModel", logger);

            AdoptionDebugLog.Write("WorkflowAdoptionDialogLauncher.Show", "resolving AdoptExistingWorkflowWindow", logger);
            var window = services.GetRequiredService<AdoptExistingWorkflowWindow>();
            AdoptionDebugLog.Write(
                "WorkflowAdoptionDialogLauncher.Show",
                $"window resolved type={window.GetType().FullName} dataContext={DescribeDataContext(window)}",
                logger);

            AdoptionDebugLog.Write("WorkflowAdoptionDialogLauncher.Show", $"assigning ProjectId={projectId}", logger);
            window.SetProject(projectId);
            window.Owner = owner;
            AdoptionDebugLog.Write(
                "WorkflowAdoptionDialogLauncher.Show",
                "LoadAsync is invoked by AdoptExistingWorkflowWindow.Loaded during ShowDialog",
                logger);
            AdoptionDebugLog.Write("WorkflowAdoptionDialogLauncher.Show", "before ShowDialog", logger);
            window.ShowDialog();
            AdoptionDebugLog.Write("WorkflowAdoptionDialogLauncher.Show", "after ShowDialog returned", logger);
        }
        catch (Exception ex)
        {
            AdoptionDebugLog.Error("WorkflowAdoptionDialogLauncher.Show", ex, logger);
            throw;
        }
    }

    private void Probe<T>(string name, IAppLogger? logger)
        where T : class
    {
        try
        {
            var service = services.GetService<T>();
            AdoptionDebugLog.Write(
                "DI",
                $"{name} null={service is null} type={service?.GetType().FullName ?? "null"}",
                logger);
        }
        catch (Exception ex)
        {
            AdoptionDebugLog.Error($"DI {name}", ex, logger);
            throw;
        }
    }

    private static string DescribeDataContext(AdoptExistingWorkflowWindow window)
    {
        if (window.Content is not FrameworkElement element)
            return "content-not-framework-element";

        return element.DataContext is null
            ? "null"
            : element.DataContext.GetType().FullName ?? "unknown";
    }
}
