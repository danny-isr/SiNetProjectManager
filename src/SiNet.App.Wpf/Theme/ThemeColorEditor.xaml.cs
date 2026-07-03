using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

public partial class ThemeColorEditor : UserControl
{
    private bool _isSyncingHexBox;

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(
            nameof(ColorHex),
            typeof(string),
            typeof(ThemeColorEditor),
            new FrameworkPropertyMetadata(
                TypographyThemeDefaults.PrimaryColor,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnColorHexChanged));

    public static readonly DependencyProperty DefaultColorHexProperty =
        DependencyProperty.Register(
            nameof(DefaultColorHex),
            typeof(string),
            typeof(ThemeColorEditor),
            new PropertyMetadata(TypographyThemeDefaults.PrimaryColor));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ThemeColorEditor));

    public ThemeColorEditor()
    {
        InitializeComponent();
        PickColorCommand = new RelayCommand(_ => PickColor());
        ResetColorCommand = new RelayCommand(_ => ColorHex = DefaultColorHex);
        HexBox.TextChanged += OnHexBoxTextChanged;
        Loaded += (_, _) =>
        {
            SyncHexBoxFromProperty();
            UpdateSwatch();
        };
    }

    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public string DefaultColorHex
    {
        get => (string)GetValue(DefaultColorHexProperty);
        set => SetValue(DefaultColorHexProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ICommand PickColorCommand { get; }
    public ICommand ResetColorCommand { get; }

    private void PickColor()
    {
        var owner = Window.GetWindow(this);
        var dialog = new WpfColorPickerDialog(ColorHex, owner);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedColorHex))
        {
            ColorHex = dialog.SelectedColorHex;
        }
    }

    private void OnHexBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingHexBox)
        {
            return;
        }

        ColorHex = HexBox.Text;
    }

    private static void OnColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ThemeColorEditor editor)
        {
            return;
        }

        editor.SyncHexBoxFromProperty();
        editor.UpdateSwatch();
        editor.GetBindingExpression(ColorHexProperty)?.UpdateSource();
    }

    private void SyncHexBoxFromProperty()
    {
        if (HexBox is null)
        {
            return;
        }

        var hex = ColorHex ?? string.Empty;
        if (string.Equals(HexBox.Text, hex, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingHexBox = true;
        try
        {
            HexBox.Text = hex;
        }
        finally
        {
            _isSyncingHexBox = false;
        }
    }

    private void UpdateSwatch()
    {
        if (ColorSwatch is null)
        {
            return;
        }

        try
        {
            ColorSwatch.Background = WpfThemeRuntimeApplier.CreateBrush(ColorHex);
        }
        catch
        {
            ColorSwatch.Background = Brushes.Transparent;
        }
    }
}
