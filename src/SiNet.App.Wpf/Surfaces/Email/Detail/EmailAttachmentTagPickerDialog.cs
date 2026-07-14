using System.Windows;
using System.Windows.Controls;
using SiNet.Application.Email.Detail;
using SiNet.Application.Settings;
using SiNet.App.Wpf.Theme;

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

        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        SetResourceReference(FontFamilyProperty, ThemeResourceKeys.FontFamily);
        SetResourceReference(FontSizeProperty, ThemeResourceKeys.TextNormalFontSize);
        SetResourceReference(ForegroundProperty, ThemeResourceKeys.ForegroundBrush);

        var list = new ListBox
        {
            ItemsSource = targets,
            DisplayMemberPath = nameof(EmailAttachmentTagTarget.DisplayName),
            Margin = new Thickness(12),
        };
        list.SetResourceReference(Control.FontFamilyProperty, ThemeResourceKeys.FontFamily);
        list.SetResourceReference(Control.FontSizeProperty, ThemeResourceKeys.TextSmallFontSize);
        list.SetResourceReference(Control.ForegroundProperty, ThemeResourceKeys.ForegroundBrush);

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is EmailAttachmentTagTarget target)
            {
                SelectedTarget = target;
                DialogResult = true;
            }
        };

        var selectButton = new Button
        {
            Content = "בחר",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(12, 0, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (TryFindResource(ThemeResourceKeys.PrimaryButtonStyle) is Style primaryStyle)
            selectButton.Style = primaryStyle;
        else
        {
            selectButton.SetResourceReference(Control.FontFamilyProperty, ThemeResourceKeys.FontFamily);
            selectButton.SetResourceReference(Control.FontSizeProperty, ThemeResourceKeys.TextNormalFontSize);
        }

        selectButton.Click += (_, _) =>
        {
            if (list.SelectedItem is EmailAttachmentTagTarget target)
            {
                SelectedTarget = target;
                DialogResult = true;
            }
        };

        var panel = new DockPanel();
        DockPanel.SetDock(selectButton, Dock.Bottom);
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
