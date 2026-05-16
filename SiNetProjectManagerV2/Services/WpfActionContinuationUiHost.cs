using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Domain.Actions.Continuation;
using SiNetSQL.Services;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.WPF_Window;
using SiNetProjectManagerV2.WPFUserControl;
using SiNetSQL.MVVM;
using WpfSiData.WPFUserControl;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// WPF implementation of <see cref="IActionContinuationUiHost"/>.
/// <para>
/// Pilot scope: only <see cref="ContinuationUiKind.WorkflowAdvanceDialog"/> is
/// implemented. Any other UI kind returns a Cancelled result.
/// </para>
/// <para>
/// For WorkflowAdvanceDialog we reuse the existing WPF confirmation prompt
/// (the same UX as the legacy <c>ActionFollowUp.WorkflowAdvanceDialog</c>
/// branch in <c>EmailManagementView</c>) so this batch does not redesign UI
/// layout. The host marshals to the UI thread, shows a Yes/No confirmation,
/// and returns a typed <see cref="WorkflowAdvanceContinuationResult"/>.
/// </para>
/// <para>
/// The host does NOT advance the workflow itself and does NOT mutate any
/// action state. The application service owns that loop.
/// </para>
/// </summary>
public sealed class WpfActionContinuationUiHost : IActionContinuationUiHost
{
    public Task<IActionContinuationResult> RequestAsync(
        IActionContinuationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case WorkflowAdvanceContinuationRequest wf:
                return ShowWorkflowAdvanceDialogAsync(wf, cancellationToken);

            case TaskCreationContinuationRequest tc:
                return ShowTaskCreationDialogAsync(tc, cancellationToken);

            case FileImportContinuationRequest fi:
                return ShowFileImportDialogAsync(fi, cancellationToken);

            case ProjectPickerContinuationRequest pp:
                return ShowProjectPickerDialogAsync(pp, cancellationToken);

            case NewProjectContinuationRequest np:
                return ShowNewProjectDialogAsync(np, cancellationToken);

            case DecisionContinuationRequest dc:
                return ShowDecisionDialogAsync(dc, cancellationToken);

            case DisciplineContinuationRequest dp:
                return ShowDisciplineDialogAsync(dp, cancellationToken);

            default:
                AppLogger.Warn(
                    $"[ContinuationHost] Unsupported continuation kind '{request.UiKind}' for action '{request.ActionCode}'. Returning Cancelled.");
                return Task.FromResult<IActionContinuationResult>(
                    new WorkflowAdvanceContinuationResult(
                        ActionCode: request.ActionCode,
                        WorkflowInstanceId: 0,
                        ConfirmAdvance: false,
                        SelectedTransitionRuleId: null,
                        Outcome: ActionContinuationOutcome.Cancelled));
        }
    }

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.TaskCreationDialog"/>.
    /// <para>
    /// Opens <see cref="TaskCreationDraftDialog"/> which returns a
    /// <see cref="TaskDraft"/> on confirm. Persistence is owned by
    /// <c>TaskCreationContinuationApplicationService</c>; this host never
    /// creates a task itself and never falls back to legacy task-creation UI.
    /// </para>
    /// </summary>
    private static Task<IActionContinuationResult> ShowTaskCreationDialogAsync(
        TaskCreationContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(BuildTaskCreationCancelled(request));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for TaskCreationDialog '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(BuildTaskCreationCancelled(request));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var dialog = new TaskCreationDraftDialog(request);
                if (owner is not null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                var ok = dialog.ShowDialog();
                if (ok == true && dialog.Result is { } draft)
                {
                    tcs.TrySetResult(new TaskCreationContinuationResult(
                        ActionCode: request.ActionCode,
                        Outcome: ActionContinuationOutcome.Confirmed,
                        Draft: draft));
                }
                else
                {
                    tcs.TrySetResult(BuildTaskCreationCancelled(request));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] TaskCreationDialog failed");
                tcs.TrySetResult(new TaskCreationContinuationResult(
                    ActionCode: request.ActionCode,
                    Outcome: ActionContinuationOutcome.Failed,
                    Draft: null));
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Show));
        }

        return tcs.Task;
    }

    private static TaskCreationContinuationResult BuildTaskCreationCancelled(TaskCreationContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            Outcome: ActionContinuationOutcome.Cancelled,
            Draft: null);

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.FileImportDialog"/>.
    /// <para>
    /// Opens <see cref="FileImportDialog"/> in draft-only mode. The dialog
    /// returns a <see cref="FileImportDraft"/>; persistence (one
    /// <c>AddMaterialToProject</c> dispatch per selection) is owned by
    /// <c>FileImportContinuationApplicationService</c>. This host never
    /// dispatches actions itself and never falls back to the legacy
    /// direct-import path for migrated actions.
    /// </para>
    /// </summary>
    private static Task<IActionContinuationResult> ShowFileImportDialogAsync(
        FileImportContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(BuildFileImportCancelled(request));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for FileImportDialog '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(BuildFileImportCancelled(request));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var dialog = new FileImportDialog(request.EmailMessageId, draftOnlyMode: true);
                if (!string.IsNullOrWhiteSpace(request.DialogTitle))
                {
                    dialog.Title = request.DialogTitle!;
                }
                if (owner is not null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                var ok = dialog.ShowDialog();
                if (ok == true && dialog.Draft is { } draft)
                {
                    tcs.TrySetResult(new FileImportContinuationResult(
                        ActionCode: request.ActionCode,
                        Outcome: ActionContinuationOutcome.Confirmed,
                        Draft: draft));
                }
                else
                {
                    tcs.TrySetResult(BuildFileImportCancelled(request));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] FileImportDialog failed");
                tcs.TrySetResult(new FileImportContinuationResult(
                    ActionCode: request.ActionCode,
                    Outcome: ActionContinuationOutcome.Failed,
                    Draft: null));
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Show));
        }

        return tcs.Task;
    }

    private static FileImportContinuationResult BuildFileImportCancelled(FileImportContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            Outcome: ActionContinuationOutcome.Cancelled,
            Draft: null);

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.ProjectPicker"/>.
    /// <para>
    /// Opens the existing <see cref="ProjectSelectorDialog"/> without redesigning
    /// the UI layout. The host only picks a project and returns it as a typed
    /// <see cref="ProjectPickerContinuationResult"/>. It performs no linking,
    /// no workflow start, no task creation, and no DB mutation beyond what the
    /// picker already does for display. The application service
    /// (<c>IProjectPickerContinuationApplicationService</c>) owns the
    /// re-dispatch of the original action with the selected ProjectId.
    /// </para>
    /// </summary>
    private static Task<IActionContinuationResult> ShowProjectPickerDialogAsync(
        ProjectPickerContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(BuildProjectPickerCancelled(request));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for ProjectPicker '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(BuildProjectPickerCancelled(request));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var dialog = new ProjectSelectorDialog();
                if (!string.IsNullOrWhiteSpace(request.Reason))
                {
                    dialog.Title = request.Reason!;
                }
                if (owner is not null && owner.IsVisible)
                {
                    dialog.Owner = owner;
                }

                var ok = dialog.ShowDialog();
                if (ok == true && dialog.SelectedProject is { } project && project.Id > 0)
                {
                    tcs.TrySetResult(new ProjectPickerContinuationResult(
                        ActionCode: request.ActionCode,
                        Outcome: ActionContinuationOutcome.Confirmed,
                        SelectedProjectId: project.Id));
                }
                else
                {
                    tcs.TrySetResult(BuildProjectPickerCancelled(request));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] ProjectPickerDialog failed");
                tcs.TrySetResult(new ProjectPickerContinuationResult(
                    ActionCode: request.ActionCode,
                    Outcome: ActionContinuationOutcome.Failed,
                    SelectedProjectId: null));
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Show));
        }

        return tcs.Task;
    }

    private static ProjectPickerContinuationResult BuildProjectPickerCancelled(ProjectPickerContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            Outcome: ActionContinuationOutcome.Cancelled,
            SelectedProjectId: null);

    private static Task<IActionContinuationResult> ShowWorkflowAdvanceDialogAsync(
        WorkflowAdvanceContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(BuildCancelled(request));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // No WPF dispatcher (e.g. test/headless host) — treat as cancel.
            return Task.FromResult<IActionContinuationResult>(BuildCancelled(request));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var prompt = BuildPrompt(request);

                MessageBoxResult result;
                if (owner is not null && owner.IsVisible)
                {
                    result = MessageBox.Show(
                        owner,
                        prompt,
                        "אישור קידום תהליך",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                }
                else
                {
                    result = MessageBox.Show(
                        prompt,
                        "אישור קידום תהליך",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                }

                var confirmed = result == MessageBoxResult.Yes;
                tcs.TrySetResult(new WorkflowAdvanceContinuationResult(
                    ActionCode: request.ActionCode,
                    WorkflowInstanceId: request.WorkflowInstanceId,
                    ConfirmAdvance: confirmed,
                    SelectedTransitionRuleId: null,
                    Outcome: confirmed
                        ? ActionContinuationOutcome.Confirmed
                        : ActionContinuationOutcome.Cancelled));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] WorkflowAdvanceDialog failed");
                tcs.TrySetResult(new WorkflowAdvanceContinuationResult(
                    ActionCode: request.ActionCode,
                    WorkflowInstanceId: request.WorkflowInstanceId,
                    ConfirmAdvance: false,
                    SelectedTransitionRuleId: null,
                    Outcome: ActionContinuationOutcome.Failed));
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Show));
        }

        return tcs.Task;
    }

    private static string BuildPrompt(WorkflowAdvanceContinuationRequest request)
    {
        return request.ActionCode switch
        {
            ActionCodes.CloseOpinion => "האם לאשר את סגירת חוות הדעת וקידום התהליך?",
            _ => "האם לאשר את קידום התהליך?",
        };
    }

    private static WorkflowAdvanceContinuationResult BuildCancelled(WorkflowAdvanceContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            WorkflowInstanceId: request.WorkflowInstanceId,
            ConfirmAdvance: false,
            SelectedTransitionRuleId: null,
            Outcome: ActionContinuationOutcome.Cancelled);

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.NewProjectDialog"/>.
    /// <para>
    /// Reuses the existing <see cref="CreateProjectUserControl"/> hosted inside
    /// a modal <see cref="Window"/>. The UserControl resolves
    /// <see cref="CreateProjectViewModel"/> from DI and exposes it via
    /// <see cref="System.Windows.FrameworkElement.DataContext"/>; this host
    /// subscribes to <c>CreateProjectViewModel.ProjectCreated</c> to capture
    /// the new <c>ProjectId</c>/<c>ProjectName</c>, closes the window, and
    /// returns a typed <see cref="NewProjectContinuationResult"/>.
    /// </para>
    /// <para>
    /// The host does not redesign the UI and does not change DB schema.
    /// Persistence and email-link / workflow-advance side effects remain
    /// inside <see cref="CreateProjectViewModel"/>; the typed result simply
    /// reports the <c>CreatedProjectId</c>. There is no legacy fallback.
    /// </para>
    /// </summary>
    private static Task<IActionContinuationResult> ShowNewProjectDialogAsync(
        NewProjectContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(BuildNewProjectCancelled(request));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for NewProjectDialog '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(BuildNewProjectCancelled(request));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var control = new CreateProjectUserControl(request.EmailMessageId);

                var window = new Window
                {
                    Title = string.IsNullOrWhiteSpace(request.DialogTitle)
                        ? "יצירת פרויקט חדש"
                        : request.DialogTitle!,
                    Content = control,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResize,
                    ShowInTaskbar = false,
                };
                if (owner is not null && owner.IsVisible)
                {
                    window.Owner = owner;
                }

                int? capturedId = null;
                string? capturedName = null;

                if (control.DataContext is CreateProjectViewModel vm)
                {
                    void OnCreated(ProjectCreatedEventArgs args)
                    {
                        capturedId = args.ProjectId;
                        capturedName = args.ProjectName;
                        try
                        {
                            window.DialogResult = true;
                        }
                        catch
                        {
                            // DialogResult only valid for ShowDialog; ignore otherwise.
                        }
                        window.Close();
                    }
                    vm.ProjectCreated += OnCreated;
                    window.Closed += (_, _) => vm.ProjectCreated -= OnCreated;
                }
                else
                {
                    AppLogger.Error(
                        $"[ContinuationHost] CreateProjectUserControl did not expose CreateProjectViewModel via DataContext for '{request.ActionCode}'. No fallback.");
                    tcs.TrySetResult(BuildNewProjectFailed(request));
                    return;
                }

                window.ShowDialog();

                if (capturedId is int pid && pid > 0)
                {
                    tcs.TrySetResult(new NewProjectContinuationResult(
                        ActionCode: request.ActionCode,
                        Outcome: ActionContinuationOutcome.Confirmed,
                        CreatedProjectId: pid,
                        CreatedProjectName: capturedName));
                }
                else
                {
                    tcs.TrySetResult(BuildNewProjectCancelled(request));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] NewProjectDialog failed");
                tcs.TrySetResult(BuildNewProjectFailed(request));
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Show));
        }

        return tcs.Task;
    }

    private static NewProjectContinuationResult BuildNewProjectCancelled(NewProjectContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            Outcome: ActionContinuationOutcome.Cancelled,
            CreatedProjectId: null,
            CreatedProjectName: null);

    private static NewProjectContinuationResult BuildNewProjectFailed(NewProjectContinuationRequest request) =>
        new(
            ActionCode: request.ActionCode,
            Outcome: ActionContinuationOutcome.Failed,
            CreatedProjectId: null,
            CreatedProjectName: null);

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.DecisionDialog"/>. Reuses the
    /// existing <see cref="ProjectDecisionsWindow"/>; persistence remains
    /// inside <c>ProjectDecisionsViewModel</c>/<c>ProjectDecisionService</c>.
    /// The host reports lifecycle outcome only (no draft).
    /// </summary>
    private static Task<IActionContinuationResult> ShowDecisionDialogAsync(
        DecisionContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(
                new DecisionContinuationResult(request.ActionCode, ActionContinuationOutcome.Cancelled));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for DecisionDialog '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(
                new DecisionContinuationResult(request.ActionCode, ActionContinuationOutcome.Cancelled));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var window = new ProjectDecisionsWindow();
                if (!string.IsNullOrWhiteSpace(request.DialogTitle))
                {
                    window.Title = request.DialogTitle!;
                }
                if (owner is not null && owner.IsVisible)
                {
                    window.Owner = owner;
                }

                window.ShowDialog();

                tcs.TrySetResult(new DecisionContinuationResult(
                    request.ActionCode,
                    ActionContinuationOutcome.Confirmed));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] DecisionDialog failed");
                tcs.TrySetResult(new DecisionContinuationResult(
                    request.ActionCode,
                    ActionContinuationOutcome.Failed));
            }
        }

        if (dispatcher.CheckAccess()) Show();
        else dispatcher.BeginInvoke(new Action(Show));

        return tcs.Task;
    }

    /// <summary>
    /// Host for <see cref="ContinuationUiKind.DisciplineDialog"/>. There is no
    /// dedicated WPF surface today; the host shows a confirmation prompt
    /// mirroring the legacy default fall-through. The typed result carries
    /// only the lifecycle outcome.
    /// </summary>
    private static Task<IActionContinuationResult> ShowDisciplineDialogAsync(
        DisciplineContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<IActionContinuationResult>(
                new DisciplineContinuationResult(request.ActionCode, ActionContinuationOutcome.Cancelled));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AppLogger.Warn(
                $"[ContinuationHost] No WPF dispatcher available for DisciplineDialog '{request.ActionCode}'. Returning Cancelled.");
            return Task.FromResult<IActionContinuationResult>(
                new DisciplineContinuationResult(request.ActionCode, ActionContinuationOutcome.Cancelled));
        }

        var tcs = new TaskCompletionSource<IActionContinuationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                var prompt = string.IsNullOrWhiteSpace(request.DialogTitle)
                    ? "האם להוסיף תחום חדש לפרויקט?"
                    : request.DialogTitle!;

                MessageBoxResult result;
                if (owner is not null && owner.IsVisible)
                {
                    result = MessageBox.Show(owner, prompt, "הוספת תחום חדש",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                }
                else
                {
                    result = MessageBox.Show(prompt, "הוספת תחום חדש",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                }

                tcs.TrySetResult(new DisciplineContinuationResult(
                    request.ActionCode,
                    result == MessageBoxResult.Yes
                        ? ActionContinuationOutcome.Confirmed
                        : ActionContinuationOutcome.Cancelled));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ContinuationHost] DisciplineDialog failed");
                tcs.TrySetResult(new DisciplineContinuationResult(
                    request.ActionCode,
                    ActionContinuationOutcome.Failed));
            }
        }

        if (dispatcher.CheckAccess()) Show();
        else dispatcher.BeginInvoke(new Action(Show));

        return tcs.Task;
    }
}
