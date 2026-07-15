using Microsoft.Extensions.DependencyInjection;

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
        return new ProjectWorkWindowView(viewModel);
    }
}
