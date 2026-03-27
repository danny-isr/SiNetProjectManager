using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Confirmation dialog shown when a downloaded file exceeds the configured
/// <c>MaxUploadFileSizeMb</c> limit. Gives the user the choice to proceed
/// with the upload as a one-time exception, or cancel it.
/// </summary>
public partial class OversizedFileDialog : Window
{
    /// <summary>
    /// <c>true</c> if the user chose to upload despite the size limit.
    /// </summary>
    public bool UserApproved { get; private set; }

    public OversizedFileDialog(string fileName, long fileSizeBytes, long limitMb)
    {
        InitializeComponent();

        FileNameText.Text = $"📄 {fileName}";
        var sizeMb = fileSizeBytes / (1024.0 * 1024.0);
        SizeInfoText.Text = $"גודל הקובץ: {sizeMb:F1} MB — המגבלה הנוכחית: {limitMb} MB";
    }

    private void UploadAnyway_Click(object sender, RoutedEventArgs e)
    {
        UserApproved = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UserApproved = false;
        DialogResult = false;
    }
}
