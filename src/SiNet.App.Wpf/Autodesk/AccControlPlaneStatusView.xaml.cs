using System.Windows;
using System.Windows.Controls;

namespace SiNet.App.Wpf.Autodesk;

public partial class AccControlPlaneStatusView : UserControl
{
    public static readonly DependencyProperty HintTextProperty =
        DependencyProperty.Register(
            nameof(HintText),
            typeof(string),
            typeof(AccControlPlaneStatusView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ModeSummaryProperty =
        DependencyProperty.Register(
            nameof(ModeSummary),
            typeof(string),
            typeof(AccControlPlaneStatusView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty KeySummaryProperty =
        DependencyProperty.Register(
            nameof(KeySummary),
            typeof(string),
            typeof(AccControlPlaneStatusView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HealthSummaryProperty =
        DependencyProperty.Register(
            nameof(HealthSummary),
            typeof(string),
            typeof(AccControlPlaneStatusView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DiagnosticsSummaryProperty =
        DependencyProperty.Register(
            nameof(DiagnosticsSummary),
            typeof(string),
            typeof(AccControlPlaneStatusView),
            new PropertyMetadata(string.Empty));

    public AccControlPlaneStatusView()
    {
        InitializeComponent();
    }

    public string? HintText
    {
        get => (string?)GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    public string ModeSummary
    {
        get => (string)GetValue(ModeSummaryProperty);
        set => SetValue(ModeSummaryProperty, value);
    }

    public string KeySummary
    {
        get => (string)GetValue(KeySummaryProperty);
        set => SetValue(KeySummaryProperty, value);
    }

    public string HealthSummary
    {
        get => (string)GetValue(HealthSummaryProperty);
        set => SetValue(HealthSummaryProperty, value);
    }

    public string DiagnosticsSummary
    {
        get => (string)GetValue(DiagnosticsSummaryProperty);
        set => SetValue(DiagnosticsSummaryProperty, value);
    }
}
