using System.Windows;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>Opens the modal Add Task dialog without coupling hosts to window types.</summary>
public interface ITaskCreateDialogFactory
{
    TaskCreateDialogResult ShowDialog(Window? owner);
}

/// <summary>Outcome of <see cref="ITaskCreateDialogFactory.ShowDialog"/>.</summary>
public readonly record struct TaskCreateDialogResult(bool Succeeded, int? CreatedTaskId);
