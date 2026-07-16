using SiNet.Application.Email.Detail;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Hostable inbox surface (list + detail) for the New System shell. Mirrors the legacy
/// <c>EmailManagementView</c> pattern: a <see cref="System.Windows.Controls.UserControl"/> that the
/// shell can cache and re-show without recreating the Gmail session.
/// </summary>
public partial class EmailSurfaceView : System.Windows.Controls.UserControl
{
    public EmailSurfaceView()
    {
        InitializeComponent();
    }

    public EmailSurfaceView(EmailWindowViewModel viewModel)
        : this()
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
    }

    public EmailWindowViewModel? ViewModel { get; }

    public void SetBodyRenderer(IEmailBodyRenderer? bodyRenderer) =>
        EmailDetailHost.SetBodyRenderer(bodyRenderer);

    public void ApplyContext(WorkSurfaceContext? context) => ViewModel?.ApplyContext(context);
}
