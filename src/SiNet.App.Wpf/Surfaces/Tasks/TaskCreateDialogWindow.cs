using System.Windows;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>Modal Add Task dialog for Task Workbench.</summary>
public sealed class TaskCreateDialogWindow : Window
{
    private readonly TaskCreateDialogViewModel _viewModel;

    public TaskCreateDialogWindow(TaskCreateDialogViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = "הוספת משימה";
        Width = 640;
        Height = 720;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new TaskCreateDialogView { DataContext = _viewModel };
        _viewModel.RequestClose += OnRequestClose;
        Closed += (_, _) => _viewModel.RequestClose -= OnRequestClose;
        Loaded += async (_, _) =>
        {
            try
            {
                await _viewModel.InitializeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppErrorReporter.Report(ex, "TaskCreateDialogWindow.OnLoaded");
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnRequestClose(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
}
