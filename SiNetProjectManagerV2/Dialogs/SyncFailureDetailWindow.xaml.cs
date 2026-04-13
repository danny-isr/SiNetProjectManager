using System.Windows;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Shows the full ErrorMessage and StackTrace for a single sync failure.
/// </summary>
public partial class SyncFailureDetailWindow : Window
{
    public SyncFailureDetailWindow(SyncFailureDisplayItem item)
    {
        InitializeComponent();

        FailedAtText.Text = item.FailedAt.ToString("yyyy-MM-dd HH:mm:ss");
        ErrorStageText.Text = item.ErrorStage;
        ErrorTypeText.Text = item.ErrorType;

        var fullText = item.ErrorMessage;
        if (!string.IsNullOrEmpty(item.StackTrace))
        {
            fullText += Environment.NewLine + Environment.NewLine
                     + "── Stack Trace ──" + Environment.NewLine
                     + item.StackTrace;
        }

        ErrorMessageBox.Text = fullText;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
