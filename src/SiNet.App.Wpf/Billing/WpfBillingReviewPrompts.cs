using System.Windows;

namespace SiNet.App.Wpf.Billing;

public sealed class WpfBillingReviewPrompts : IBillingReviewPrompts
{
    public bool ConfirmPrepareBill(string projectLabel)
    {
        var text =
            $"לסמן את {projectLabel} להכנת חשבון?\nפעולה זו אינה יוצרת חשבון ב-MasterPlan ואינה קובעת סכום.";
        return Ask(text, "להכין חשבון") == MessageBoxResult.Yes;
    }

    public BillingNotNowPromptResult? PromptNotNow(string projectLabel)
    {
        var dialog = new BillingNotNowDialog(projectLabel);
        if (Owner() is Window owner)
            dialog.Owner = owner;
        return dialog.ShowDialog() == true
            ? new BillingNotNowPromptResult(dialog.Reason, dialog.ReviewAgainDate)
            : null;
    }

    public bool ConfirmClearDecision(string projectLabel)
    {
        var text =
            $"לבטל את החלטת הניהול עבור {projectLabel}?\nהפרויקט יחזור למצב המחושב. אין שינוי ב-MasterPlan.";
        return Ask(text, "בטל החלטה") == MessageBoxResult.Yes;
    }

    private static MessageBoxResult Ask(string text, string caption)
    {
        return Owner() is Window owner
            ? MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
    }

    private static Window? Owner() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}
