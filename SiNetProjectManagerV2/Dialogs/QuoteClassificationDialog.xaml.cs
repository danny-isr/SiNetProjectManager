using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Constants;
using SiNetSQL.Data;
using SiNetSQL.Services.EmailIngestion;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Classification host for <c>IdentifyQuoteRequest</c> tasks. Shows the source
/// email's subject/from/date and reconciled attachments (reusing the same data
/// path as <see cref="EmailPreviewWindow"/>), and lets the operator pick one of
/// two explicit verdicts: <see cref="TaskResultCodes.QuoteRequestDetected"/> or
/// <see cref="TaskResultCodes.NotQuoteRequest"/>. The selected code is exposed
/// on <see cref="SelectedResultCode"/>; the caller drives task completion
/// through the existing <c>ITaskCompletionCoordinator</c> path.
/// </summary>
public partial class QuoteClassificationDialog : Window
{
    private readonly int _emailMessageId;

    /// <summary>The picked result code, or null if the user cancelled.</summary>
    public string? SelectedResultCode { get; private set; }

    public QuoteClassificationDialog(int emailMessageId)
    {
        InitializeComponent();
        _emailMessageId = emailMessageId;
        Loaded += (_, _) => _ = LoadEmailDataAsync();
    }

    private async Task LoadEmailDataAsync()
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory != null)
            {
                using var db = dbFactory.CreateDbContext();
                var email = db.EmailInboxMessages
                    .AsNoTracking()
                    .Where(m => m.Id == _emailMessageId)
                    .Select(m => new { m.Id, m.Subject, m.FromAddress, m.ReceivedUtc })
                    .FirstOrDefault();

                if (email != null)
                {
                    Title = $"סיווג בקשת הצעת מחיר — מייל #{email.Id}";
                    SubjectText.Text = email.Subject ?? "(ללא נושא)";
                    FromText.Text = $"מאת: {email.FromAddress}";
                    DateText.Text = $"תאריך: {email.ReceivedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
                }
                else
                {
                    SubjectText.Text = $"מייל #{_emailMessageId} לא נמצא";
                }
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

    private void QuoteRequest_Click(object sender, RoutedEventArgs e)
    {
        SelectedResultCode = TaskResultCodes.QuoteRequestDetected;
        DialogResult = true;
        Close();
    }

    private void NotQuote_Click(object sender, RoutedEventArgs e)
    {
        SelectedResultCode = TaskResultCodes.NotQuoteRequest;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedResultCode = null;
        DialogResult = false;
        Close();
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

        try
        {
            var viewer = new ExternalBrowserWindow(url, null) { Title = $"📄 {att.FileName}", Width = 1200, Height = 800 };
            viewer.Show();
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
