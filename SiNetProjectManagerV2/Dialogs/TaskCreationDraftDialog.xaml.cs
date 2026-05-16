using System;
using System.Windows;
using SiNetSQL.Domain.Actions.Continuation;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Minimal WPF dialog that builds and returns a <see cref="TaskDraft"/>
/// for the TaskCreationDialog typed continuation flow. Does NOT persist —
/// persistence is owned by <c>TaskCreationContinuationApplicationService</c>.
/// </summary>
public partial class TaskCreationDraftDialog : Window
{
    private readonly TaskCreationContinuationRequest _request;

    /// <summary>
    /// Populated when the user confirms a valid draft; otherwise <c>null</c>.
    /// </summary>
    public TaskDraft? Result { get; private set; }

    public TaskCreationDraftDialog(TaskCreationContinuationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(request.DialogTitle))
        {
            Title = request.DialogTitle!;
        }

        var prefill = request.Prefill;
        ProjectInfoText.Text = $"פרויקט #{prefill.ProjectId}";
        TitleBox.Text = prefill.Title ?? string.Empty;
        BodyBox.Text = prefill.Body ?? string.Empty;
        DueDatePicker.SelectedDate = prefill.DueDate;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var title = (TitleBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorText.Text = "יש להזין כותרת למשימה.";
            ErrorText.Visibility = Visibility.Visible;
            TitleBox.Focus();
            return;
        }

        var prefill = _request.Prefill;
        Result = prefill with
        {
            Title = title,
            Body = string.IsNullOrWhiteSpace(BodyBox.Text) ? prefill.Body : BodyBox.Text,
            DueDate = DueDatePicker.SelectedDate ?? prefill.DueDate,
        };

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }
}
