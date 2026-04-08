using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Lightweight floating window that displays email metadata.
/// Shown alongside the project creation form so the user can
/// see which email triggered the action.
/// </summary>
public partial class EmailPreviewWindow : Window
{
    public EmailPreviewWindow(int emailMessageId)
    {
        InitializeComponent();
        LoadEmailData(emailMessageId);
    }

    private void LoadEmailData(int emailMessageId)
    {
        try
        {
            var dbFactory = App.ServiceProvider?.GetService<IDbContextFactory<SiNetSQLDbContext>>();
            if (dbFactory == null) return;

            using var db = dbFactory.CreateDbContext();
            var email = db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.Id == emailMessageId)
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
                SubjectText.Text = $"מייל #{emailMessageId} לא נמצא";
            }
        }
        catch
        {
            SubjectText.Text = $"שגיאה בטעינת מייל #{emailMessageId}";
        }
    }
}
