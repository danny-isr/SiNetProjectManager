using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.WorkSurfaces;

/// <summary>
/// Maps <see cref="WorkSurfaceContext.ComponentKey"/> to native work surfaces in the New System shell.
/// Reuses <see cref="ITaskNavigationService"/> — no parallel router.
/// </summary>
public interface IWorkSurfaceLauncher
{
    /// <summary>Opens the surface that matches <paramref name="context"/>.</summary>
    ValueTask<bool> TryOpenAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default);

    /// <summary>Resolves a task and opens its work surface.</summary>
    ValueTask<bool> TryOpenFromTaskAsync(int taskId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkSurfaceLauncher(IServiceProvider services) : IWorkSurfaceLauncher
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public async ValueTask<bool> TryOpenFromTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var navigation = _services.GetService<ITaskNavigationService>();
        if (navigation is null)
        {
            return false;
        }

        var context = await navigation.ResolveAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return false;
        }

        return await TryOpenAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> TryOpenAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            if (context.TaskId is > 0 && context.PrimaryWorkTargetEntityId is > 0
                && _services.GetService<IEmailWorkItemWindowFactory>() is { } workItemFactory)
            {
                var workItemWindow = workItemFactory.Create();
                workItemWindow.ApplyContext(context);
                workItemWindow.Show();
                return ValueTask.FromResult(true);
            }

            var factory = _services.GetRequiredService<IEmailWindowFactory>();
            var window = factory.Create();
            window.ApplyContext(context);
            window.Show();
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }
}
