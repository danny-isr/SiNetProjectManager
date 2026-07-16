using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.ProjectWork;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

public interface IProjectWorkWindowFactory
{
    ProjectWorkWindowView Create();
}

public sealed class ProjectWorkWindowFactory(IServiceProvider services) : IProjectWorkWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public ProjectWorkWindowView Create()
    {
        var viewModel = _services.GetRequiredService<ProjectWorkWindowViewModel>();
        var view = new ProjectWorkWindowView(viewModel);

        // Optional host-seam: the embedded ACC viewer is only registered by hosts that ship WebView2
        // (production V2 host). When absent, the surface uses the external-browser fallback.
        var accViewerHost = _services.GetService<IAccViewerHost>();
        view.SetAccViewerHost(accViewerHost);

        return view;
    }
}
