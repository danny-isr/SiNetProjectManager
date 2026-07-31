using System.Windows;
using SiNet.Application.Abstractions.Email;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Surfaces.Tasks;

public partial class QuoteSourceEmailPickerDialog : Window
{
    public sealed record PickerRow(EmailSummary Summary, string Display);

    public QuoteSourceEmailPickerDialog(IReadOnlyList<EmailSummary> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);

        EmailList.ItemsSource = items
            .Select(i => new PickerRow(
                i,
                $"{i.ReceivedAt:yyyy-MM-dd HH:mm} · {i.From.Value} · {i.Subject}"))
            .ToList();
    }

    public EmailSummary? Selected { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (EmailList.SelectedItem is not PickerRow row)
        {
            MessageBox.Show("יש לבחור מייל.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Selected = row.Summary;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
