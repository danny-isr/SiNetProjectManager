using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public partial class EmailAttachmentStripView : UserControl
{
    public EmailAttachmentStripView() => InitializeComponent();

    private void AttachmentLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;
        if (sender is not FrameworkElement { DataContext: EmailDetailAttachmentItem item })
            return;

        // #region agent log
        AgentDebugNdjson.Write(
            "H-O1",
            "EmailAttachmentStripView.AttachmentLabel_MouseLeftButtonDown",
            "double-click attachment",
            new Dictionary<string, object?>
            {
                ["fileName"] = item.FileName,
                ["canOpen"] = item.CanOpenInAcc,
                ["hasAccItemId"] = !string.IsNullOrWhiteSpace(item.AccItemId),
                ["commandCanExecute"] = item.OpenInAccCommand.CanExecute(null),
            });
        // #endregion

        if (!item.OpenInAccCommand.CanExecute(null))
            return;

        item.OpenInAccCommand.Execute(null);
        e.Handled = true;
    }

    private async void AlternativeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox
            || comboBox.DataContext is not EmailDetailAttachmentItem item)
        {
            return;
        }

        // #region agent log
        try
        {
            var sv = comboBox.SelectedValue;
            var detail =
                $"att={item.InboxAttachmentId} svType={sv?.GetType().FullName ?? "null"} sv={sv?.ToString() ?? "null"} bound={item.SelectedAlternativeId?.ToString() ?? "null"} pf={item.ProjectFileId?.ToString() ?? "null"}";
            WorkflowDebugTrace.Step("Email.TagUI", $"H-ALT3 selection-changed {detail}");
            var payload = JsonSerializer.Serialize(new
            {
                sessionId = "cbfc8f",
                runId = "quote-file-tag-pre",
                hypothesisId = "H-ALT3",
                location = "EmailAttachmentStripView.AlternativeSelectionChanged",
                message = detail,
                data = new { detail },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            File.AppendAllText(@"D:\repos2026\debug-cbfc8f.log", payload + Environment.NewLine);
        }
        catch
        {
            // diagnostics only
        }
        // #endregion

        if (comboBox.SelectedValue is not int selectedId)
        {
            return;
        }

        if (item.SelectedAlternativeId == selectedId)
        {
            return;
        }

        item.SelectedAlternativeId = selectedId;
        if (item.AlternativeChangedCommand.CanExecute(null))
        {
            item.AlternativeChangedCommand.Execute(null);
        }
    }
}
