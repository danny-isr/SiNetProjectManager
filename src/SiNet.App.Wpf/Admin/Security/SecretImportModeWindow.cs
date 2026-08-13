using System.Text;
using System.Windows;
using System.Windows.Controls;
using SiNet.Application.Configuration;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.Security;

/// <summary>Shared mode picker for admin Secret Setup and employee workstation import.</summary>
internal sealed class SecretImportModeWindow : Window
{
    private readonly RadioButton _upsert;
    private readonly RadioButton _replace;

    public SecretImportModeWindow(SecretImportPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Title = "ייבוא מפתחות — בחירת מצב";
        Width = 520;
        Height = 420;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);

        _upsert = new RadioButton
        {
            Content = "עדכן את כל מה שמופיע בקובץ",
            IsChecked = true,
            Margin = new Thickness(0, 8, 0, 4),
        };
        _replace = new RadioButton
        {
            Content = "החלף — השאר רק מה שקיים בקובץ",
            Margin = new Thickness(0, 4, 0, 8),
        };

        var previewBlock = new TextBlock
        {
            Text = BuildPreviewText(preview),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button { Content = "המשך", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "ביטול", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel();
        body.Children.Add(previewBlock);
        body.Children.Add(_upsert);
        body.Children.Add(_replace);
        if (preview.CatalogKeysAbsentFromFile.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "בהחלפה יימחקו: " + string.Join(", ", preview.CatalogKeysAbsentFromFile),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.8,
            });
        }

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    public SecretImportMode SelectedMode =>
        _replace.IsChecked == true
            ? SecretImportMode.ReplaceCatalogWithFile
            : SecretImportMode.UpsertFromFile;

    internal static string BuildPreviewText(SecretImportPreviewDto preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"מפתחות בקובץ: {preview.KeysToImportCount}");
        foreach (var item in preview.Items)
        {
            var status = item.ExistsInVault ? "(קיים בתחנה)" : "(חדש)";
            sb.AppendLine($"• {item.DisplayName} {status}");
        }

        if (preview.UnknownKeyCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"מפתחות לא מוכרים שידולגו: {preview.UnknownKeyCount}");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Shows the mode picker. Replace with pending deletes requires a second named confirm.
    /// Returns <see langword="null"/> when the user cancels.
    /// </summary>
    internal static SecretImportMode? ChooseMode(Window? owner, SecretImportPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var dialog = new SecretImportModeWindow(preview);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var mode = dialog.SelectedMode;
        if (mode == SecretImportMode.ReplaceCatalogWithFile
            && preview.CatalogKeysAbsentFromFile.Count > 0)
        {
            var names = string.Join(Environment.NewLine, preview.CatalogKeysAbsentFromFile.Select(k => "• " + k));
            var confirm = MessageBox.Show(
                owner,
                "יימחקו המפתחות הבאים מהתחנה הזו:" + Environment.NewLine + names + Environment.NewLine + Environment.NewLine
                + "להמשיך בהחלפה?",
                "אישור מחיקה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return null;
            }
        }

        return mode;
    }
}
