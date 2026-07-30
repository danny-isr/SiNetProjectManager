using System.Windows;
using System.Windows.Controls;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

/// <summary>Simple standalone prompt for naming a new project alternative.</summary>
internal sealed class WpfEmailAlternativeNamePromptHost : IEmailAlternativeNamePromptHost
{
    public bool IsAvailable => true;

    public Task<string?> PromptForNewAlternativeNameAsync(
        IReadOnlyList<string> existingNames,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<string?>(null);
        }

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(Prompt(existingNames));
        }

        return dispatcher.InvokeAsync(() => Prompt(existingNames)).Task;
    }

    private static string? Prompt(IReadOnlyList<string> existingNames)
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current?.MainWindow;

        var box = new TextBox { Margin = new Thickness(12, 0, 12, 8) };
        var ok = new Button
        {
            Content = "אישור",
            Width = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var cancel = new Button
        {
            Content = "ביטול",
            Width = 90,
            IsCancel = true,
        };

        var dialog = new Window
        {
            Title = "שם חלופה חדשה",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FlowDirection = FlowDirection.RightToLeft,
            Owner = owner is { IsVisible: true } ? owner : null,
        };

        string? result = null;
        ok.Click += (_, _) =>
        {
            var name = box.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(dialog, "יש להזין שם.", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (existingNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(dialog, "שם זה כבר קיים.", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = name;
            dialog.DialogResult = true;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 12, 12),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "שם החלופה:",
            Margin = new Thickness(12, 12, 12, 4),
        };
        Grid.SetRow(label, 0);
        Grid.SetRow(box, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        return dialog.ShowDialog() == true ? result : null;
    }
}
