using System.Windows;
using SiNet.App.Wpf.Surfaces.Email.Detail;
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
        DataContext = shellViewModel.EmailDetail;
        DetailHost.DataContext = shellViewModel.EmailDetail;
        DetailHost.SetBodyRenderer(bodyRenderer);
    }

    public EmailDetailViewModel DetailViewModel => _shellViewModel.EmailDetail;

    public void ApplyContext(WorkSurfaceContext? context) => _shellViewModel.ApplyContext(context);
}
