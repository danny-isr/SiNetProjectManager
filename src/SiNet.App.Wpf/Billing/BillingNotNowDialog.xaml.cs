using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Billing;

public partial class BillingNotNowDialog : Window
{
    public BillingNotNowDialog(string projectLabel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        ProjectLabelText.Text = projectLabel;
    }

    public string Reason => ReasonBox.Text.Trim();
    public DateTime? ReviewAgainDate => ReviewDatePicker.SelectedDate?.Date;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            MessageBox.Show(
                "חובה לציין סיבה להשהיה.",
                "לא עכשיו",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
