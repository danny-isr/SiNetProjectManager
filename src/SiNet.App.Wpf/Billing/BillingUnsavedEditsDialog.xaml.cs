using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Billing;

public partial class BillingUnsavedEditsDialog : Window
{
    public BillingUnsavedEditsDialog()
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
    }

    public BillingUnsavedEditsDecision Decision { get; private set; } = BillingUnsavedEditsDecision.Stay;

    private void Stay_Click(object sender, RoutedEventArgs e)
    {
        Decision = BillingUnsavedEditsDecision.Stay;
        DialogResult = false;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Decision = BillingUnsavedEditsDecision.Discard;
        DialogResult = true;
        Close();
    }
}
