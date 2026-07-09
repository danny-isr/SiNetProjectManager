using System.Windows;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

/// <summary>
/// Native project-file picker for attachment tagging (replaces legacy host window).
/// </summary>
public sealed class EmailAttachmentTagPickerDialog : Window
{
    public EmailAttachmentTagPickerDialog(IReadOnlyList<EmailAttachmentTagTarget> targets)
    {
        Title = "בחר סוג קובץ פרויקט";
        Width = 420;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;

        var list = new System.Windows.Controls.ListBox
        {
            ItemsSource = targets,
            DisplayMemberPath = nameof(EmailAttachmentTagTarget.DisplayName),
            Margin = new Thickness(12),
        };

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is EmailAttachmentTagTarget target)
            {
                SelectedTarget = target;
                DialogResult = true;
            }
        };

        var selectButton = new System.Windows.Controls.Button
        {
            Content = "בחר",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(12, 0, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        selectButton.Click += (_, _) =>
        {
            if (list.SelectedItem is EmailAttachmentTagTarget target)
            {
                SelectedTarget = target;
                DialogResult = true;
            }
        };

        var panel = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(selectButton, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(selectButton);
        panel.Children.Add(list);
        Content = panel;
    }

    public EmailAttachmentTagTarget? SelectedTarget { get; private set; }

    public static EmailAttachmentTagTarget? ShowDialog(
        Window owner,
        IReadOnlyList<EmailAttachmentTagTarget> targets)
    {
        var dialog = new EmailAttachmentTagPickerDialog(targets) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedTarget : null;
    }
}
