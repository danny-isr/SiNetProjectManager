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

    public async ValueTask<bool> TryOpenAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            // Task-driven email opens require an exact primary work target — never browse fallback.
            if (context.TaskId is > 0)
            {
                if (context.PrimaryWorkTargetEntityId is not > 0)
                {
                    Trace.TraceWarning(
                        "[WorkSurfaceLauncher] Email task {0} has no primary work target; opening blocked.",
                        context.TaskId);
                    return false;
                }

                if (_services.GetService<IEmailWorkItemWindowFactory>() is not { } workItemFactory)
                {
                    Trace.TraceWarning("[WorkSurfaceLauncher] IEmailWorkItemWindowFactory is not registered.");
                    return false;
                }

                var workItemWindow = workItemFactory.Create();
                workItemWindow.ApplyContext(context);
                workItemWindow.Show();
                return true;
            }

            // Browse / project-centric email (no TaskId): full inbox window.
            var factory = _services.GetRequiredService<IEmailWindowFactory>();
            var window = factory.Create();
            window.ApplyContext(context);
            window.Show();
            return true;
        }

        if (string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport, StringComparison.OrdinalIgnoreCase))
        {
            if (context.PrimaryWorkTargetEntityId is not > 0)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] Inspection task {0} has no report target; opening blocked.",
                    context.TaskId);
                return false;
            }

            if (_services.GetService<IInspectionWindowFactory>() is not { } inspectionFactory)
            {
                Trace.TraceWarning("[WorkSurfaceLauncher] IInspectionWindowFactory is not registered.");
                return false;
            }

            var inspectionWindow = inspectionFactory.Create();
            var opened = await inspectionWindow.ApplyContextAsync(context, cancellationToken).ConfigureAwait(false);
            if (!opened)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] Inspection task {0} failed to load report #{1}.",
                    context.TaskId,
                    context.PrimaryWorkTargetEntityId);
                return false;
            }

            inspectionWindow.Show();
            return true;
        }

        Trace.TraceWarning(
            "[WorkSurfaceLauncher] Unsupported component key '{0}' for task {1}. No surface registered.",
            context.ComponentKey,
            context.TaskId);
        return false;
    }
}
