using System.Windows;
using System.Windows.Controls;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public partial class EmailAttachmentStripView : UserControl
{
    public EmailAttachmentStripView() => InitializeComponent();

    private async void AlternativeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox
            || comboBox.DataContext is not EmailDetailAttachmentItem item
            || comboBox.SelectedValue is not int selectedId)
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
