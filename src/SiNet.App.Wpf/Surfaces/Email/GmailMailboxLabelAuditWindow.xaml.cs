using System.Windows;
using SiNet.Application.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

public partial class GmailMailboxLabelAuditWindow : Window
{
    public GmailMailboxLabelAuditWindow(IReadOnlyList<GmailMailboxLabelAuditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        InitializeComponent();
        ViewModel = new GmailMailboxLabelAuditViewModel(rows);
        DataContext = ViewModel;
    }

    internal GmailMailboxLabelAuditViewModel ViewModel { get; }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var names = AuditGrid.SelectedItems
            .OfType<GmailMailboxLabelAuditRow>()
            .Select(static r => r.LabelName)
            .ToArray();
        if (names.Length == 0 && ViewModel.SelectedRow is { } selected)
        {
            names = [selected.LabelName];
        }

        ViewModel.CopyNames(names);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
