using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Services.EmailIngestion;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Floating window that displays email metadata and its attachments.
/// Attachments can be opened via ACC viewer URL.
/// </summary>
public partial class EmailPreviewWindow : Window
{
    private readonly int _emailMessageId;

    public EmailPreviewWindow(int emailMessageId)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        LoadEmailData();
    }

    private async void LoadEmailData()
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var db = dbFactory.CreateDbContext();
            var email = db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == _emailMessageId)
                .Select(m => new { m.Id, m.Subject, m.FromAddress, m.ReceivedUtc })
                .FirstOrDefault();

            if (email != null)
            {
                Title = $"📧 מייל #{email.Id}";
                SubjectText.Text = email.Subject ?? "(ללא נושא)";
                FromText.Text = $"מאת: {email.FromAddress}";
                DateText.Text = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
            }
            else
            {
                SubjectText.Text = $"מייל #{_emailMessageId} לא נמצא";
            }

            var reconciler = App.ServiceProvider?.GetService<IAccInboxReconciliationService>();
            var reconciled = reconciler is null
                ? null
                : await reconciler.ReconcileByMessageIdAsync(_emailMessageId);

            var attachments = reconciled?.Attachments
                .OrderBy(a => a.AttachmentIndex)
                .Select(a => new AttachmentDisplayItem
                {
                    Id = a.InboxAttachmentId ?? 0,
                    FileName = a.FileName,
                    AccItemId = a.AccItemId,
                    AccVersionId = a.AccVersionId,
                    InboxAccProjectId = reconciled.InboxAccProjectId,
                    InboxAccFolderId = reconciled.InboxAccFolderId,
                    ExistsInAcc = a.ExistsInAcc,
                    Status = a.Status.ToString(),
                    StatusText = a.StatusText,
                })
                .ToList() ?? [];

            if (attachments.Count > 0)
            {
                AttachmentsHeader.Text = $"📎 קבצים מצורפים ({attachments.Count}):";
                AttachmentsList.ItemsSource = attachments;
            }
            else
            {
                AttachmentsHeader.Text = "📎 אין קבצים מצורפים";
                AttachmentsList.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            SubjectText.Text = $"שגיאה: {ex.Message}";
        }
    }

    private void OpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AttachmentDisplayItem att }) return;

        if (!att.ExistsInAcc || string.IsNullOrEmpty(att.AccItemId))
        {
            MessageBox.Show($"הקובץ אינו זמין לפתיחה ב-ACC. סטטוס: {att.StatusText}", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Build ACC Docs URL
        if (string.IsNullOrEmpty(att.InboxAccProjectId))
        {
            MessageBox.Show("מזהה פרויקט ACC לא נמצא.", "לא זמין",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var projectGuid = att.InboxAccProjectId.StartsWith("b.", StringComparison.Ordinal)
            ? att.InboxAccProjectId[2..] : att.InboxAccProjectId;

        var url = $"https://acc.autodesk.com/docs/files/projects/{projectGuid}";
        if (!string.IsNullOrEmpty(att.InboxAccFolderId))
            url += $"?folderUrn={Uri.EscapeDataString(att.InboxAccFolderId)}&entityId={Uri.EscapeDataString(att.AccItemId)}";

        OpenInBrowser(url, att.FileName);
    }

    private void OpenInBrowser(string url, string title)
    {
        try
        {
            var viewer = new ExternalBrowserWindow(url, null) { Title = $"📄 {title}", Width = 1200, Height = 800 };
            viewer.Show();
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}

/// <summary>Simple display item for attachment list.</summary>
public class AttachmentDisplayItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? AccItemId { get; set; }
    public string? AccVersionId { get; set; }
    public string? InboxAccProjectId { get; set; }
    public string? InboxAccFolderId { get; set; }
    public bool ExistsInAcc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
}
