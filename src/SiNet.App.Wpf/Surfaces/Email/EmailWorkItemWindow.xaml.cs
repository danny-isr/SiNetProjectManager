using System.Windows;
using SiNet.App.Wpf.Surfaces.Email.Detail;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Email.Detail;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Task-driven email work surface: detail pane only with minimal chrome.
/// </summary>
public partial class EmailWorkItemWindow : Window
{
    private readonly EmailWindowViewModel _shellViewModel;

    public EmailWorkItemWindow(EmailWindowViewModel shellViewModel, IEmailBodyRenderer? bodyRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);
        _shellViewModel = shellViewModel;
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        // Shell VM owns StatusMessage (locate / connect failures). Detail pane keeps its own DC.
        DataContext = shellViewModel;
        DetailHost.DataContext = shellViewModel.EmailDetail;
        DetailHost.SetBodyRenderer(bodyRenderer);

        // Close this popup host when the task-driven filing flow completes (file + move + task close).
        shellViewModel.EmailDetail.WorkItemDismissRequested += OnWorkItemDismissRequested;
        Closed += (_, _) => shellViewModel.EmailDetail.WorkItemDismissRequested -= OnWorkItemDismissRequested;
        Loaded += OnLoadedApplyComplementaryLayout;
    }

    private void OnLoadedApplyComplementaryLayout(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedApplyComplementaryLayout;
        TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(this, Owner);
        Activate();
    }

    private void OnWorkItemDismissRequested() => Close();

    public EmailDetailViewModel DetailViewModel => _shellViewModel.EmailDetail;

    public void ApplyContext(WorkSurfaceContext? context) => _shellViewModel.ApplyContext(context);
}
