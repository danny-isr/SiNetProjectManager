using Microsoft.Extensions.DependencyInjection;



namespace SiNet.App.Wpf.Surfaces.Tasks;



/// <summary>

/// Default factory for <see cref="TaskWorkbenchView"/>.

/// </summary>

public sealed class TaskPanelReadOnlyWindowFactory(IServiceProvider services) : ITaskPanelReadOnlyWindowFactory

{

    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));



    public TaskWorkbenchView Create()

    {

        var viewModel = _services.GetRequiredService<TaskWorkbenchViewModel>();

        return new TaskWorkbenchView(viewModel);

    }

}


