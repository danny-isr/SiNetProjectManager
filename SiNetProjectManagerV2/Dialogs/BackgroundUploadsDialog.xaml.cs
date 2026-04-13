using System.Windows;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Status dialog shown when the user tries to close the application while
/// background ACC uploads are still in progress.
/// <para>
/// Options:
/// <list type="bullet">
///   <item><b>Close when done</b> — enters waiting mode; auto-closes the app when uploads finish.</item>
///   <item><b>Force close</b> — closes immediately (uploads may be lost).</item>
///   <item><b>Cancel</b> — returns to the application without closing.</item>
/// </list>
/// </para>
/// </summary>
public partial class BackgroundUploadsDialog : Window
{
    private bool _waitingForCompletion;

    public BackgroundUploadsDialog()
    {
        InitializeComponent();
        UpdateCountText(EmailManagementViewModel.ActiveUploadCount);
        EmailManagementViewModel.ActiveUploadsChanged += OnActiveUploadsChanged;
    }

    private void OnActiveUploadsChanged(int count)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateCountText(count);

            if (_waitingForCompletion && count == 0)
            {
                // All uploads finished — allow the app to close
                DialogResult = true;
            }
        });
    }

    private void UpdateCountText(int count)
    {
        CountText.Text = count == 1
            ? "תהליך אחד פעיל ברקע..."
            : $"{count} תהליכים פעילים ברקע...";
    }

    private void CloseWhenDone_Click(object sender, RoutedEventArgs e)
    {
        _waitingForCompletion = true;
        CloseWhenDoneButton.IsEnabled = false;
        ForceCloseButton.IsEnabled = true;
        InfoText.Text = "התוכנה תיסגר אוטומטית כשכל התהליכים יסתיימו.";

        // Edge case: uploads already finished between button click and here
        if (EmailManagementViewModel.ActiveUploadCount == 0)
        {
            DialogResult = true;
        }
    }

    private void ForceClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        EmailManagementViewModel.ActiveUploadsChanged -= OnActiveUploadsChanged;
        base.OnClosed(e);
    }
}
