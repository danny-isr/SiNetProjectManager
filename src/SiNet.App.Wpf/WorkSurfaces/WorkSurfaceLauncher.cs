using System.Diagnostics;
using System.Windows;

using Microsoft.Extensions.DependencyInjection;

using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email;
using SiNet.Application.Email.QuoteSend;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using Microsoft.Extensions.Logging;

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
    private readonly IAppLogger? _logger = services.GetService<IAppLogger>();

    private void WarnMissing(string message)
    {
        if (_logger is not null)
            _logger.Warn(message);
        else
            Trace.TraceWarning(message);
    }

    public async ValueTask<bool> TryOpenFromTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var navigation = _services.GetService<ITaskNavigationService>();
        if (navigation is null)
        {
            WarnMissing("[WorkSurfaceLauncher] outcome=Failed kind=NotRegistered service=ITaskNavigationService");
            return false;
        }

        // Resolve off the UI thread; open surfaces must hop back to STA.
        var context = await navigation.ResolveAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            WarnMissing($"[WorkSurfaceLauncher] outcome=Failed kind=UnresolvedTask task={taskId}");
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

        // Proposal task-type hosts must run before generic ComponentKey routing
        // (OpenQuoteProject shares ProjectCreationFromEmail with the email surface).
        if (string.Equals(context.TaskTypeCode, "IdentifyQuoteRequest", StringComparison.OrdinalIgnoreCase))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open",
                $"task={context.TaskId} → routing to QuoteClassification dialog (email={context.PrimaryWorkTargetEntityId})");

            if (_services.GetService<ITaskCompletionService>() is not { } completion)
            {
                WarnMissing("[WorkSurfaceLauncher] ITaskCompletionService is not registered.");
                return false;
            }

            return ShowTaskDialog(
                TaskSurfaceWindowKind.QuoteClassification,
                context.TaskId,
                () => new QuoteClassificationDialog(
                    context,
                    completion,
                    _services.GetService<IEmailInboxQueryService>()));
        }

        // OpenReviewProject reuses the same ProjectSetup-from-email dialog + decision service
        // (completion event Review.ProjectCreated). Without this branch it falls through to the
        // generic email surface and never shows the create-project form.
        if (string.Equals(context.TaskTypeCode, "OpenQuoteProject", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.TaskTypeCode, "OpenReviewProject", StringComparison.OrdinalIgnoreCase))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open",
                $"task={context.TaskId} type={context.TaskTypeCode} → routing to OpenQuoteProject combined dialog (email={context.PrimaryWorkTargetEntityId})");

            if (_services.GetService<IOpenQuoteProjectDecisionService>() is not { } openQuoteDecisionService)
            {
                WarnMissing("[WorkSurfaceLauncher] IOpenQuoteProjectDecisionService is not registered.");
                return false;
            }

            if (_services.GetService<ProjectCreateDialogViewModel>() is not { } createVm
                || _services.GetService<IPlaceCatalogService>() is not { } places
                || _services.GetService<ICompanyCatalogService>() is not { } companies)
            {
                WarnMissing("[WorkSurfaceLauncher] Project create services are not registered.");
                return false;
            }

            return ShowTaskDialog(
                TaskSurfaceWindowKind.OpenQuoteProject,
                context.TaskId,
                () => new OpenQuoteProjectDecisionDialog(
                    context,
                    openQuoteDecisionService,
                    createVm,
                    places,
                    companies,
                    _services.GetService<IEmailInboxQueryService>(),
                    _services.GetService<IAccResolvedDocsUrlLauncher>(),
                    _services.GetService<SiNet.Application.Email.IEmailFilingService>(),
                    _services.GetService<IEmailGateway>()));
        }

        if (string.Equals(context.TaskTypeCode, "SendQuoteToClient", StringComparison.OrdinalIgnoreCase))
        {
            WorkflowDebugTrace.Step("Launcher.Open",
                $"task={context.TaskId} → routing to SendQuoteToClient dialog");

            if (_services.GetService<ITaskCompletionService>() is not { } sendQuoteCompletion)
            {
                WarnMissing("[WorkSurfaceLauncher] ITaskCompletionService is not registered.");
                return false;
            }

            return ShowTaskDialog(
                TaskSurfaceWindowKind.SendQuoteToClient,
                context.TaskId,
                () => new SendQuoteToClientDialog(
                    context,
                    sendQuoteCompletion,
                    _services.GetService<IQuoteSendComposeService>(),
                    _services.GetService<IQuoteSendAttachmentService>(),
                    _services.GetService<IEmailGateway>(),
                    _services.GetService<IAuthorizationQueryService>(),
                    _services.GetService<ILoggerFactory>()?.CreateLogger("SendQuoteToClient")));
        }

        if (string.Equals(context.TaskTypeCode, "FollowQuoteApproval", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.TaskTypeCode, "FollowWorkOrder", StringComparison.OrdinalIgnoreCase))
        {
            return await OpenEmailFirstFollowTaskAsync(context, cancellationToken).ConfigureAwait(true);
        }

        if (WorkSurfaceComponentKeys.IsEmailSurface(context.ComponentKey))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open", $"task={context.TaskId} → routing to EMAIL surface");

            // Task-driven email opens require an exact primary work target — never browse fallback.
            // Exception: EmailHints (FollowQuote / FollowWorkOrder) allow open without an inbox TaskLink.
            if (context.TaskId is > 0)
            {
                if (context.PrimaryWorkTargetEntityId is not > 0 && context.EmailHints is null)
                {
                    WarnMissing(
                        $"[WorkSurfaceLauncher] Email task {context.TaskId} has no primary work target; opening blocked.");
                    return false;
                }

                if (_services.GetService<EmailWorkItemTaskFloatingHost>() is not { } emailHost)
                {
                    WarnMissing("[WorkSurfaceLauncher] EmailWorkItemTaskFloatingHost is not registered.");
                    return false;
                }

                return emailHost.OpenOrRebind(context);
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

            // PerformProfessionalReview may open in report-creation mode when no report is linked yet
            // (documented product path). Follow-up report tasks still require an exact report id.
            if (context.PrimaryWorkTargetEntityId is not > 0
                && !AllowsInspectionReportCreationWhenMissing(context.TaskTypeCode))
            {
                WarnMissing(
                    $"[WorkSurfaceLauncher] Inspection task {context.TaskId} has no report target; opening blocked.");
                return false;
            }

            if (_services.GetService<InspectionTaskFloatingHost>() is not { } inspectionHost)
            {
                WarnMissing("[WorkSurfaceLauncher] InspectionTaskFloatingHost is not registered.");
                return false;
            }

            return await inspectionHost.OpenOrRebindAsync(context, cancellationToken).ConfigureAwait(true);
        }

        if (WorkSurfaceComponentKeys.IsProjectWorkSurface(context.ComponentKey))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Launcher.Open", $"task={context.TaskId} → routing to PROJECT-WORK surface");

            // Project-scoped task surface (native replacement for legacy ShowProjectWork). Requires a
            // project; the file workspace itself is a later gated phase (task shell + completion here).
            if (context.ProjectId <= 0)
            {
                WarnMissing(
                    $"[WorkSurfaceLauncher] ProjectWork task {context.TaskId} has no project; opening blocked.");
                return false;
            }

            // Prefer the shell-hosted cached surface (main content area) when the host provides it.
            if (_services.GetService<IProjectWorkSurfaceHost>() is { } projectWorkHost)
            {
                var hosted = await projectWorkHost.TryOpenFromTaskAsync(context, cancellationToken).ConfigureAwait(true);
                if (hosted)
                    return true;
            }

            if (_services.GetService<ProjectWorkTaskFloatingHost>() is not { } floatingHost)
            {
                WarnMissing("[WorkSurfaceLauncher] ProjectWorkTaskFloatingHost is not registered.");
                return false;
            }

            // Fallback: same process-wide singleton (never a second unmanaged Window).
            return await floatingHost.OpenOrRebindAsync(context, cancellationToken).ConfigureAwait(true);
        }

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Launcher.Open",
            $"task={context.TaskId} componentKey={context.ComponentKey} UNSUPPORTED — no surface registered");
        WarnMissing(
            $"[WorkSurfaceLauncher] Unsupported component key '{context.ComponentKey}' for task {context.TaskId}. No surface registered.");
        return false;
    }

    private async Task<bool> OpenEmailFirstFollowTaskAsync(
        WorkSurfaceContext context,
        CancellationToken cancellationToken)
    {
        var isWorkOrder = string.Equals(
            context.TaskTypeCode,
            "FollowWorkOrder",
            StringComparison.OrdinalIgnoreCase);
        var routeLabel = isWorkOrder ? "FollowWorkOrder" : "FollowQuoteApproval";

        WorkflowDebugTrace.Step(
            "Launcher.Open",
            $"task={context.TaskId} → routing to {routeLabel} Email-first");

        FollowQuoteOpenAnchor? anchor = null;
        if (context.TaskId is int followTaskId && followTaskId > 0
            && _services.GetService<IFollowQuoteAnchorResolver>() is { } resolver)
        {
            // Soft anchor: latest QuoteSendProof on the task's project (same resolver as FollowQuote).
            anchor = await resolver.ResolveAsync(followTaskId, cancellationToken).ConfigureAwait(true);
        }

        var hints = new EmailOpenHints(
            GmailThreadId: anchor?.GmailThreadId,
            AfterGmailMessageId: anchor?.SentGmailMessageId,
            CounterpartAddress: anchor?.CounterpartAddress,
            OfferProjectWorkFallback: true);

        var emailContext = context with
        {
            ComponentKey = WorkSurfaceComponentKeys.EmailFiling,
            EmailHints = hints,
        };

        // Close any other task surface; Email-first uses the shell Email list (filter + empty state).
        _services.GetService<ITaskSurfaceWindowCoordinator>()
            ?.PrepareOpen(TaskSurfaceWindowKind.EmailWorkItem, context.TaskId);

        if (_services.GetService<IEmailSurfaceHost>() is { } shellEmail)
        {
            shellEmail.Show(emailContext);
            WorkflowDebugTrace.Step(
                $"{routeLabel}.Open",
                $"task={context.TaskId} host=shellEmail thread={(hints.GmailThreadId ?? "-")} to={(hints.CounterpartAddress ?? "-")}");
            return true;
        }

        if (_services.GetService<EmailWorkItemTaskFloatingHost>() is not { } emailHost)
        {
            WarnMissing($"[WorkSurfaceLauncher] No Email host for {routeLabel}.");
            return false;
        }

        return emailHost.OpenOrRebind(emailContext);
    }

    private bool ShowTaskDialog(
        TaskSurfaceWindowKind kind,
        int? taskId,
        Func<Window> createDialog)
    {
        ArgumentNullException.ThrowIfNull(createDialog);

        var coordinator = _services.GetService<ITaskSurfaceWindowCoordinator>();
        var existing = coordinator?.PrepareOpen(kind, taskId);
        if (existing is { IsLoaded: true })
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return true;
        }

        var dialog = createDialog();
        TaskSurfaceWindowLayout.PrepareTaskSurfaceWindow(dialog);
        coordinator?.RegisterActive(dialog, kind, taskId);

        // Task Workbench is Topmost; task dialogs are not. Lower the workbench while the modal
        // dialog runs so ShowDialog is not blocked behind an unreachable window.
        var workbench = FindLoadedTaskWorkbench();
        var restoreTopmost = workbench is { Topmost: true };
        if (restoreTopmost)
            workbench!.Topmost = false;

        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            if (restoreTopmost && workbench is { IsLoaded: true })
                workbench.Topmost = true;
        }

        return true;
    }

    private static TaskWorkbenchView? FindLoadedTaskWorkbench()
    {
        if (System.Windows.Application.Current?.Windows is not { } windows)
            return null;

        foreach (Window window in windows)
        {
            if (window is TaskWorkbenchView { IsLoaded: true } workbench)
                return workbench;
        }

        return null;
    }

    /// <summary>
    /// Only <c>PerformProfessionalReview</c> may open without a linked report (creation mode).
    /// Follow-up report tasks require an exact report id — never auto-pick.
    /// </summary>
    private static bool AllowsInspectionReportCreationWhenMissing(string? taskTypeCode) =>
        string.Equals(taskTypeCode, "PerformProfessionalReview", StringComparison.Ordinal);
}
