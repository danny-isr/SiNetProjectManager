using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

public sealed class WpfColorPickerDialog : Window
{
    private readonly string _originalHex;
    private readonly Action<string>? _previewColorChanged;
    private readonly Border _preview;
    private readonly Slider _red;
    private readonly Slider _green;
    private readonly Slider _blue;
    private readonly TextBox _hexBox;
    private bool _isSyncingHexBox;

    public WpfColorPickerDialog(string initialHex, Window? owner, Action<string>? previewColorChanged = null)
    {
        _originalHex = initialHex;
        _previewColorChanged = previewColorChanged;

        Title = "בחר צבע";
        Width = 360;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        FlowDirection = FlowDirection.RightToLeft;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);

        var initial = ParseColor(initialHex);

        _preview = new Border
        {
            Height = 48,
            Margin = new Thickness(12, 12, 12, 8),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
        };

        _red = CreateSlider(initial.R);
        _green = CreateSlider(initial.G);
        _blue = CreateSlider(initial.B);

        _hexBox = new TextBox
        {
            Margin = new Thickness(12, 0, 12, 8),
            Text = ToHex(initial),
        };

        var ok = new Button { Content = "אישור", Width = 80, Margin = new Thickness(0, 0, 8, 12), IsDefault = true };
        var cancel = new Button { Content = "ביטול", Width = 80, Margin = new Thickness(0, 0, 12, 12), IsCancel = true };

        ok.Click += (_, _) =>
        {
            SelectedColorHex = _hexBox.Text.Trim();
            DialogResult = true;
        };

        var panel = new StackPanel();
        panel.Children.Add(_preview);
        panel.Children.Add(CreateLabeledRow("R", _red));
        panel.Children.Add(CreateLabeledRow("G", _green));
        panel.Children.Add(CreateLabeledRow("B", _blue));
        panel.Children.Add(new TextBlock { Text = "Hex", Margin = new Thickness(12, 0, 12, 4) });
        panel.Children.Add(_hexBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;

        _red.ValueChanged += (_, _) => SyncFromSliders();
        _green.ValueChanged += (_, _) => SyncFromSliders();
        _blue.ValueChanged += (_, _) => SyncFromSliders();
        _hexBox.TextChanged += (_, _) => SyncFromHex();

        UpdatePreview(initial);
    }

    public string? SelectedColorHex { get; private set; }

    internal void TestSyncFromSliders() => SyncFromSliders();

    internal void TestSetRgb(byte red, byte green, byte blue)
    {
        _red.Value = red;
        _green.Value = green;
        _blue.Value = blue;
        SyncFromSliders();
    }

    internal string OriginalHex => _originalHex;

    private void SyncFromSliders()
    {
        var color = Color.FromRgb((byte)_red.Value, (byte)_green.Value, (byte)_blue.Value);
        var hex = ToHex(color);

        _isSyncingHexBox = true;
        try
        {
            _hexBox.Text = hex;
        }
        finally
        {
            _isSyncingHexBox = false;
        }

        UpdatePreview(color);
        NotifyPreview(hex);
    }

    private void SyncFromHex()
    {
        if (_isSyncingHexBox)
        {
            return;
        }

        try
        {
            var hex = _hexBox.Text.Trim();
            if (!TypographyThemeDefaults.IsValidHexColor(hex))
            {
                return;
            }

            var color = ParseColor(hex);
            _red.Value = color.R;
            _green.Value = color.G;
            _blue.Value = color.B;
            UpdatePreview(color);
            NotifyPreview(hex);
        }
        catch
        {
            // ignore invalid hex while typing
        }
    }

    private void NotifyPreview(string hex)
    {
        if (_previewColorChanged is null || !TypographyThemeDefaults.IsValidHexColor(hex))
        {
            return;
        }

        _previewColorChanged(hex);
    }

    private void UpdatePreview(Color color)
        => _preview.Background = new SolidColorBrush(color);

    private static Slider CreateSlider(byte value) => new()
    {
        Minimum = 0,
        Maximum = 255,
        Value = value,
        Margin = new Thickness(0, 0, 12, 0),
    };

    private static Grid CreateLabeledRow(string label, Slider slider)
    {
        var grid = new Grid { Margin = new Thickness(12, 0, 12, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(caption);
        grid.Children.Add(slider);
        return grid;
    }

    private static Color ParseColor(string hex)
    {
        var normalized = WpfThemeRuntimeApplier.NormalizeHex(hex);
        return (Color)ColorConverter.ConvertFromString(normalized)!;
    }

    private static string ToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
