using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>Minimal modal string prompt for ProjectWork folder/file actions.</summary>
internal sealed class StringPromptDialog : Window
{
    private readonly TextBox _input;

    private StringPromptDialog(string title, string prompt, string? initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _input = new TextBox { Text = initial ?? string.Empty, Margin = new Thickness(0, 8, 0, 12) };
        _input.SelectAll();

        var ok = new Button { Content = "אישור", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "ביטול", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel },
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_input);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => Keyboard.Focus(_input);
    }

    public string Value => _input.Text?.Trim() ?? string.Empty;

    public static string? Prompt(Window? owner, string title, string prompt, string? initial = null)
    {
        var dlg = new StringPromptDialog(title, prompt, initial);
        if (owner is not null)
            dlg.Owner = owner;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
