using System.Windows;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Status dialog shown when the user tries to close the application while
/// background ACC uploads are still in progress.
/// </summary>
public partial class BackgroundUploadsDialog : Window
{
    private bool _waitingForCompletion;

    public BackgroundUploadsDialog()
    {
        InitializeComponent();
        UpdateCountText(AccBackgroundWorkMonitor.TotalActiveCount);
        AccBackgroundWorkMonitor.TotalActiveCountChanged += OnActiveUploadsChanged;
    }

    private void OnActiveUploadsChanged(int count)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateCountText(count);

            if (_waitingForCompletion && count == 0)
            {
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

        if (AccBackgroundWorkMonitor.TotalActiveCount == 0)
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
        AccBackgroundWorkMonitor.TotalActiveCountChanged -= OnActiveUploadsChanged;
        base.OnClosed(e);
    }
}
