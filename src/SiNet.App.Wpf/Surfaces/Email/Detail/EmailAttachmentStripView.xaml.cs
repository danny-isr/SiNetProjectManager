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

        if (comboBox.SelectedValue is int selectedPreviewId)
        {
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-ALT3 selection-changed att={item.InboxAttachmentId} sv={selectedPreviewId} bound={item.SelectedAlternativeId?.ToString() ?? "null"}");
        }

        if (comboBox.SelectedValue is not int selectedId)
        {
            return;
        }

        // CreateNew (-1) must always raise AlternativeChanged even when TwoWay binding already
        // wrote SelectedAlternativeId=-1 — otherwise HandleCreateNewAlternativeAsync never runs.
        if (selectedId == EmailProjectAlternativeOption.CreateNewId
            || item.AvailableAlternatives.Any(a => a.IsCreateNew && a.Id == selectedId))
        {
            item.SelectedAlternativeId = selectedId;
            if (item.AlternativeChangedCommand.CanExecute(null))
            {
                item.AlternativeChangedCommand.Execute(null);
            }

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
