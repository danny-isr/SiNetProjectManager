using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Default factory for <see cref="TaskWorkbenchView"/> with a process-wide live singleton.
/// </summary>
public sealed class TaskPanelReadOnlyWindowFactory(IServiceProvider services) : ITaskPanelReadOnlyWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly object _gate = new();
    private TaskWorkbenchView? _live;

    public TaskWorkbenchView Create()
    {
        var viewModel = _services.GetRequiredService<TaskWorkbenchViewModel>();
        return new TaskWorkbenchView(viewModel);
    }

    public TaskWorkbenchView ShowOrActivate()
    {
        lock (_gate)
        {
            if (_live is { IsLoaded: true })
            {
                if (_live.WindowState == WindowState.Minimized)
                    _live.WindowState = WindowState.Normal;

                _live.Activate();
                if (_live.DataContext is TaskWorkbenchViewModel vm
                    && vm.RefreshCommand.CanExecute(null))
                {
                    vm.RefreshCommand.Execute(null);
                }

                return _live;
            }

            var window = Create();
            window.Closed += OnLiveClosed;
            _live = window;
        }

        var shown = _live!;
        if (System.Windows.Application.Current?.MainWindow is { } owner
            && !ReferenceEquals(owner, shown))
        {
            shown.Owner = owner;
        }

        shown.ApplyTallNarrowLayout();
        shown.Show();
        shown.Activate();
        return shown;
    }

    private void OnLiveClosed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_live, sender))
                _live = null;
        }

        if (sender is Window window)
            window.Closed -= OnLiveClosed;
    }
}
