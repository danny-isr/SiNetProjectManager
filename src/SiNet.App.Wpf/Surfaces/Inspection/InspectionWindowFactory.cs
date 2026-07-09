using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

public interface IInspectionWindowFactory
{
    InspectionWindowView Create();
}

public sealed class InspectionWindowFactory(IServiceProvider services) : IInspectionWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public InspectionWindowView Create()
    {
        var viewModel = _services.GetRequiredService<InspectionWindowViewModel>();
        return new InspectionWindowView(viewModel);
    }
}
