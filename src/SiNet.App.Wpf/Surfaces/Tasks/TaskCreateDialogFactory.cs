using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>DI-backed factory for <see cref="TaskCreateDialogWindow"/>.</summary>
public sealed class TaskCreateDialogFactory(IServiceProvider services) : ITaskCreateDialogFactory
{
    public TaskCreateDialogResult ShowDialog(Window? owner)
    {
        var viewModel = services.GetRequiredService<TaskCreateDialogViewModel>();
        var window = new TaskCreateDialogWindow(viewModel) { Owner = owner };
        var dialogResult = window.ShowDialog();
        return new TaskCreateDialogResult(dialogResult == true, viewModel.CreatedTaskId);
    }
}
