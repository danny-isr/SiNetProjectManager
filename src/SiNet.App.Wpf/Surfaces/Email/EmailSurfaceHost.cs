using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Email.Detail;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <inheritdoc />
public sealed class EmailSurfaceHost(
    IServiceProvider services,
    IShellContentHost contentHost) : IEmailSurfaceHost
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly IShellContentHost _contentHost = contentHost ?? throw new ArgumentNullException(nameof(contentHost));

    private EmailSurfaceView? _view;
    private EmailWindowViewModel? _viewModel;

    /// <inheritdoc />
    public void Show(WorkSurfaceContext? context = null)
    {
        EnsureCreated();

        if (context is not null)
        {
            _view!.ApplyContext(context);
        }

        _contentHost.NavigateTo(_view);

        if (System.Windows.Application.Current?.MainWindow is { } main)
        {
            if (main.WindowState == WindowState.Minimized)
            {
                main.WindowState = WindowState.Normal;
            }

            main.Activate();
        }
    }

    /// <inheritdoc />
    public EmailWindowViewModel? TryGetViewModel() => _viewModel;

    /// <inheritdoc />
    public bool TryBlockShellClose(Window owner)
    {
        if (_viewModel is null)
        {
            return false;
        }

        return _viewModel.TryBlockCloseForBackgroundWork(owner);
    }

    private void EnsureCreated()
    {
        if (_view is not null)
        {
            return;
        }

        _viewModel = _services.GetRequiredService<EmailWindowViewModel>();
        _view = new EmailSurfaceView(_viewModel);
        var bodyRenderer = _services.GetService<IEmailBodyRenderer>();
        // #region agent log
        SiNet.Application.Diagnostics.AgentDebugNdjson.Write(
            "H1",
            "EmailSurfaceHost.EnsureCreated",
            "IEmailBodyRenderer resolve",
            new Dictionary<string, object?>
            {
                ["resolved"] = bodyRenderer is not null,
                ["rendererType"] = bodyRenderer?.GetType().FullName,
                ["isAvailable"] = bodyRenderer?.IsAvailable,
            },
            runId: "email-viewer-debug");
        // #endregion
        _view.SetBodyRenderer(bodyRenderer);
    }
}
