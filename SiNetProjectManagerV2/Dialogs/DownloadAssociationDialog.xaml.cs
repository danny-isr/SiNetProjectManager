using System.Windows;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Dialog shown when a file is downloaded from a link within an email (e.g., Jumbo Mail, WeTransfer).
/// Asks the user whether to associate the file with the email's linked project.
/// <para>
/// Results:
/// <list type="bullet">
///   <item><b>AssociateToProject</b>: Save to ACC-mirrored path and upload to project.</item>
///   <item><b>SaveToDownloads</b>: Save to the system Downloads folder (no project link).</item>
///   <item><b>Cancel</b>: Cancel the download entirely.</item>
/// </list>
/// </para>
/// </summary>
public partial class DownloadAssociationDialog : Window
{
    /// <summary>
    /// The user's chosen action for the downloaded file.
    /// </summary>
    public DownloadAction ChosenAction { get; private set; } = DownloadAction.Cancel;

    public DownloadAssociationDialog(string fileName, string? projectName)
    {
        InitializeComponent();

        FileNameText.Text = $"📄 {fileName}";

        if (!string.IsNullOrEmpty(projectName))
        {
            ProjectNameText.Text = projectName;
        }
        else
        {
            // No project associated — hide project info panel
            ProjectInfoBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UploadToAcc_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = DownloadAction.UploadToAcc;
        DialogResult = true;
    }

    private void SaveToDownloads_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = DownloadAction.SaveToDownloads;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ChosenAction = DownloadAction.Cancel;
        DialogResult = false;
    }
}

/// <summary>
/// Represents the user's choice for handling a downloaded file from an email link.
/// </summary>
public enum DownloadAction
{
    /// <summary>Upload to ACC Inbox (ACC-mirrored local path + ACC upload pipeline).</summary>
    UploadToAcc,

    /// <summary>Associate file with the email's project (ACC-mirrored path). Legacy — same as UploadToAcc.</summary>
    AssociateToProject,

    /// <summary>Save to system Downloads folder only (no ACC upload).</summary>
    SaveToDownloads,

    /// <summary>Cancel the download entirely.</summary>
    Cancel
}
