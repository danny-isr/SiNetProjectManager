using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
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
            Trace.TraceWarning("[WorkSurfaceLauncher] ITaskNavigationService is not registered.");
            return false;
        }

        var context = await navigation.ResolveAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            Trace.TraceWarning("[WorkSurfaceLauncher] Task {0} could not be resolved to a work surface.", taskId);
            return false;
        }

        return await TryOpenAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> TryOpenAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            if (context.TaskId is > 0 && context.PrimaryWorkTargetEntityId is not > 0)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] Email task {0} has no primary work target; opening blocked.",
                    context.TaskId);
                return ValueTask.FromResult(false);
            }

            if (context.TaskId is > 0 && context.PrimaryWorkTargetEntityId is > 0
                && _services.GetService<IEmailWorkItemWindowFactory>() is { } workItemFactory)
            {
                var workItemWindow = workItemFactory.Create();
                workItemWindow.ApplyContext(context);
                workItemWindow.Show();
                return ValueTask.FromResult(true);
            }

            // TEMPORARY / DEFERRED — non-task email opens fall back to the full inbox window.
            // WHY: browse mode still uses the list-first shell until work-item-only browse exists.
            // REMOVAL WHEN: browse vs task-mode entry points are split at every caller.
            var factory = _services.GetRequiredService<IEmailWindowFactory>();
            var window = factory.Create();
            window.ApplyContext(context);
            window.Show();
            return ValueTask.FromResult(true);
        }

        if (string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport, StringComparison.OrdinalIgnoreCase))
        {
            if (context.PrimaryWorkTargetEntityId is not > 0)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] Inspection task {0} has no report target; opening blocked.",
                    context.TaskId);
                return ValueTask.FromResult(false);
            }

            if (_services.GetService<IInspectionWindowFactory>() is not { } inspectionFactory)
            {
                Trace.TraceWarning("[WorkSurfaceLauncher] IInspectionWindowFactory is not registered.");
                return ValueTask.FromResult(false);
            }

            var inspectionWindow = inspectionFactory.Create();
            inspectionWindow.ApplyContext(context);
            inspectionWindow.Show();
            return ValueTask.FromResult(true);
        }

        Trace.TraceWarning(
            "[WorkSurfaceLauncher] No surface registered for component key '{0}' (task {1}).",
            context.ComponentKey,
            context.TaskId);
        return ValueTask.FromResult(false);
    }
}
