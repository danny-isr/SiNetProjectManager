using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Admin-only popup listing recent daily sync failures from SiData.dbo.Sync_RunFailures.
/// Shown at startup when failures exist in the last 7 days.
/// DESIGN NOTE: This is a floating, non-modal notification window that does NOT block the application.
/// Closing this window does not affect the application lifecycle.
/// </summary>
public partial class SyncFailuresWindow : Window
{
    public SyncFailuresWindow(List<SyncFailureDisplayItem> failures)
    {
        InitializeComponent();
        SubHeaderText.Text = $"{failures.Count} failure(s) found.";
        FailuresGrid.ItemsSource = failures;
        FailuresGrid.SelectionChanged += FailuresGrid_SelectionChanged;

        // Ensure this window doesn't control application shutdown
        // By NOT setting Owner, this becomes an independent top-level window
    }

    private void FailuresGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsButton.IsEnabled = FailuresGrid.SelectedItem != null;
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (FailuresGrid.SelectedItem is SyncFailureDisplayItem item)
        {
            var detailWindow = new SyncFailureDetailWindow(item) { Owner = this };
            detailWindow.ShowDialog();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// Lightweight display model for the failures grid.
/// Keeps UI decoupled from the EF entity.
/// </summary>
public sealed class SyncFailureDisplayItem
{
    public DateTime FailedAt { get; init; }
    public string ErrorStage { get; init; } = string.Empty;
    public string ErrorType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string? StackTrace { get; init; }

    /// <summary>
    /// Truncated message for the DataGrid column (first 120 chars).
    /// </summary>
    public string ErrorMessageTruncated =>
        ErrorMessage.Length > 120 ? string.Concat(ErrorMessage.AsSpan(0, 120), "…") : ErrorMessage;
}
