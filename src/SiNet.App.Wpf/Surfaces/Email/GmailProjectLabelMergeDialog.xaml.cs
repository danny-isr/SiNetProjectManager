using System.Windows;
using SiNet.Application.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

public partial class GmailProjectLabelMergeDialog : Window
{
    public GmailProjectLabelMergeDialog(
        GmailMailboxLabelAuditRow source,
        IReadOnlyList<GmailMailboxLabelAuditRow> targets,
        GmailMailboxLabelAuditRow recommended)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(recommended);
        InitializeComponent();

        SourceRow = source;
        TitleText.Text = $"מיזוג תוויות פרויקט {source.ParsedProjectNumber}";
        SourceText.Text = FormatRow(source);
        var items = targets.Select(row => new TargetOption(row, row.LabelId == recommended.LabelId)).ToList();
        TargetCombo.ItemsSource = items;
        TargetCombo.SelectedItem = items.FirstOrDefault(item => item.IsRecommended) ?? items[0];
    }

    public GmailMailboxLabelAuditRow SourceRow { get; }

    public GmailMailboxLabelAuditRow? TargetRow => (TargetCombo.SelectedItem as TargetOption)?.Row;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (TargetRow is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"מיזוג תוויות פרויקט {SourceRow.ParsedProjectNumber}{Environment.NewLine}{Environment.NewLine}"
            + $"מקור:{Environment.NewLine}{FormatRow(SourceRow)}{Environment.NewLine}{Environment.NewLine}"
            + $"יעד:{Environment.NewLine}{FormatRow(TargetRow)}{Environment.NewLine}{Environment.NewLine}"
            + "כל ההודעות מהמקור יקבלו את תווית היעד."
            + Environment.NewLine
            + "לאחר השלמת ההעברה ואימותה תווית המקור תימחק.",
            "אישור מיזוג",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
        {
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

    private static string FormatRow(GmailMailboxLabelAuditRow row)
    {
        var count = row.MessageCount is int n ? $"{n} הודעות" : "מספר הודעות לא זמין";
        return $"{row.LabelName}{Environment.NewLine}{count}";
    }

    private sealed record TargetOption(GmailMailboxLabelAuditRow Row, bool IsRecommended)
    {
        public string Display
        {
            get
            {
                var count = Row.MessageCount is int n ? $"{n} הודעות" : "הודעות לא זמין";
                var recommend = IsRecommended ? " (מומלץ — נתיב תקין)" : string.Empty;
                return $"{Row.LabelName} · {count}{recommend}";
            }
        }
    }
}
