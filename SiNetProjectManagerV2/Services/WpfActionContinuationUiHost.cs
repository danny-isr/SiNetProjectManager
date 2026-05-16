using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Domain.Actions.Continuation;
using SiNetSQL.Services;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.WPFUserControl;

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
}
