using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Dialog shown when a task transitions from Active (actionable) to Waiting (non-actionable).
/// Prompts the user to document what action was performed before handing off.
/// Results: Save (note required) / Skip (no note) / Cancel (abort status change).
/// </summary>
public partial class ActionProofDialog : Window
{
    /// <summary>
    /// The note text entered by the user. Null if cancelled or skipped.
    /// </summary>
    public string? ActionNote { get; private set; }

    /// <summary>
    /// True if the user chose Save or Skip (proceed with status change).
    /// False if the user cancelled (abort status change).
    /// </summary>
    public bool Confirmed { get; private set; }

    public ActionProofDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NoteTextBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var note = NoteTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            MessageBox.Show("יש להזין תיאור פעולה כדי לשמור.", "שדה חובה",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            NoteTextBox.Focus();
            return;
        }

        ActionNote = note;
        Confirmed = true;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ActionNote = null;
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
