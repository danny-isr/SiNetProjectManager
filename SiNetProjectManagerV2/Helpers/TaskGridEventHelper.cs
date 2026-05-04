using System.Windows;
using System.Windows.Controls;
using SiNetSQL.Models;
using SiNetProjectManagerV2.Dialogs;

namespace SiNetProjectManagerV2.Helpers;

/// <summary>
/// Shared WPF event logic for task grid controls (<see cref="TaskPanelView"/> and
/// <see cref="FloatingProjectTasksView"/>). Eliminates duplicated status-change handling.
/// </summary>
public static class TaskGridEventHelper
{
    /// <summary>
    /// Result of processing a status change in the UI.
    /// </summary>
    public enum StatusChangeResult
    {
        /// <summary>No actual change detected (skip).</summary>
        NoChange,
        /// <summary>User cancelled the action-proof dialog (revert).</summary>
        Cancelled,
        /// <summary>Ready to commit — call ViewModel.UpdateTaskStatusInline.</summary>
        Commit,
    }

    /// <summary>
    /// Extracts and validates a status ComboBox selection change, prompting for an action-proof
    /// dialog when transitioning from actionable → waiting.
    /// <para>
    /// Returns <see cref="StatusChangeResult.Commit"/> with the resolved parameters when the
    /// caller should proceed with <c>ViewModel.UpdateTaskStatusInline</c>.
    /// </para>
    /// </summary>
    public static StatusChangeResult ProcessStatusChange(
        SelectionChangedEventArgs e,
        ComboBox combo,
        Window ownerWindow,
        out ProjectAssignment? task,
        out ProjectAssignmentStatus? newStatus,
        out int? oldStatusId,
        out string? actionNote,
        out int? taskResultId)
    {
        task = null;
        newStatus = null;
        oldStatusId = null;
        actionNote = null;
        taskResultId = null;

        if (combo.Tag is not ProjectAssignment t) return StatusChangeResult.NoChange;
        if (e.AddedItems.Count == 0) return StatusChangeResult.NoChange;
        if (e.AddedItems[0] is not ProjectAssignmentStatus ns) return StatusChangeResult.NoChange;

        task = t;
        newStatus = ns;

        // Check if this is initial load (no removed items means first load)
        if (e.RemovedItems.Count == 0) return StatusChangeResult.NoChange;

        // Get original status ID
        oldStatusId = e.RemovedItems[0] is ProjectAssignmentStatus oldStatus
            ? oldStatus.Id
            : task.StatusId;

        // Skip if no actual change
        if (oldStatusId == newStatus.Id) return StatusChangeResult.NoChange;

        // Detect Active → Waiting transition (ball leaving our court)
        bool wasActionable = task.AssignmentStatus?.IsActionable ?? false;
        bool willBeWaiting = newStatus.IsOpen && !newStatus.IsActionable;

        if (wasActionable && willBeWaiting)
        {
            var dialog = new ActionProofDialog { Owner = ownerWindow };
            var result = dialog.ShowDialog();

            if (result != true || !dialog.Confirmed)
                return StatusChangeResult.Cancelled;

            actionNote = dialog.ActionNote;
        }

        // Detect closing transition (open → closed) and prompt for an optional TaskResult
        // so workflow rules can branch on the recorded business outcome.
        bool wasOpen = task.AssignmentStatus?.IsOpen ?? true;
        bool willBeClosed = !newStatus.IsOpen;

        if (wasOpen && willBeClosed)
        {
            var picker = new TaskResultPickerDialog { Owner = ownerWindow };
            var pickerResult = picker.ShowDialog();

            if (pickerResult != true)
                return StatusChangeResult.Cancelled;

            taskResultId = picker.SelectedResult?.Id;
        }

        return StatusChangeResult.Commit;
    }
}
