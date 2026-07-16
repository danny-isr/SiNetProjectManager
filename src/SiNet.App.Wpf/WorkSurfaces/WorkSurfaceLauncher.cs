using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Email;
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

        // Resolve off the UI thread; open surfaces must hop back to STA.
        var context = await navigation.ResolveAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            Trace.TraceWarning("[WorkSurfaceLauncher] Task {0} could not be resolved to a work surface.", taskId);
            return false;
        }

        return await TryOpenAsync(context, cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask<bool> TryOpenAsync(WorkSurfaceContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Window/dialog creation requires the WPF STA UI thread. Callers often reach here after
        // ConfigureAwait(false) (e.g. task resolve), so always marshal before touching UI.
        return await UiThread.RunAsync(() => OpenOnUiThreadAsync(context, cancellationToken)).ConfigureAwait(true);
    }

    private async Task<bool> OpenOnUiThreadAsync(WorkSurfaceContext context, CancellationToken cancellationToken)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Launcher.Open",
            $"task={context.TaskId} componentKey={context.ComponentKey} project={context.ProjectId} primaryTarget={context.PrimaryWorkTargetEntityId}");

        if (WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open", $"task={context.TaskId} → routing to EMAIL surface");

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

            // Browse / project-centric email (no TaskId): hosted singleton inbox in the main shell.
            if (_services.GetService<IEmailSurfaceHost>() is { } emailSurfaceHost)
            {
                emailSurfaceHost.Show(context);
                return true;
            }

            // Fallback when shell host is unavailable (standalone / tests): popup window.
            var factory = _services.GetRequiredService<IEmailWindowFactory>();
            var window = factory.Create();
            window.ApplyContext(context);
            window.Show();
            return true;
        }

        if (string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport, StringComparison.OrdinalIgnoreCase))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open", $"task={context.TaskId} → routing to INSPECTION surface");

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
            var opened = await inspectionWindow.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
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

        // Classification-only Proposal intake — native dialog (mirrors legacy QuoteClassificationDialog).
        // Must run before ProjectWork routing so Task Workbench does not open an empty project shell.
        if (string.Equals(context.TaskTypeCode, "IdentifyQuoteRequest", StringComparison.OrdinalIgnoreCase))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open",
                $"task={context.TaskId} → routing to QuoteClassification dialog (email={context.PrimaryWorkTargetEntityId})");

            if (_services.GetService<ITaskCompletionService>() is not { } completion)
            {
                Trace.TraceWarning("[WorkSurfaceLauncher] ITaskCompletionService is not registered.");
                return false;
            }

            var dialog = new QuoteClassificationDialog(
                context,
                completion,
                _services.GetService<IEmailInboxQueryService>())
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            dialog.ShowDialog();
            return true;
        }

        if (WorkSurfaceComponentKeys.IsProjectWorkSurface(context.ComponentKey))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open", $"task={context.TaskId} → routing to PROJECT-WORK surface");

            // Project-scoped task surface (native replacement for legacy ShowProjectWork). Requires a
            // project; the file workspace itself is a later gated phase (task shell + completion here).
            if (context.ProjectId <= 0)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] ProjectWork task {0} has no project; opening blocked.",
                    context.TaskId);
                return false;
            }

            if (_services.GetService<IProjectWorkWindowFactory>() is not { } projectWorkFactory)
            {
                Trace.TraceWarning("[WorkSurfaceLauncher] IProjectWorkWindowFactory is not registered.");
                return false;
            }

            var projectWorkWindow = projectWorkFactory.Create();
            var projectOpened = await projectWorkWindow.ApplyContextAsync(context, cancellationToken).ConfigureAwait(true);
            if (!projectOpened)
            {
                Trace.TraceWarning(
                    "[WorkSurfaceLauncher] ProjectWork task {0} could not open for project #{1}.",
                    context.TaskId,
                    context.ProjectId);
                return false;
            }

            projectWorkWindow.Show();
            return true;
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Launcher.Open",
            $"task={context.TaskId} componentKey={context.ComponentKey} UNSUPPORTED — no surface registered");
        Trace.TraceWarning(
            "[WorkSurfaceLauncher] Unsupported component key '{0}' for task {1}. No surface registered.",
            context.ComponentKey,
            context.TaskId);
        return false;
    }
}
