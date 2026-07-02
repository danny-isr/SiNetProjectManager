using System.Windows;
using System.Windows.Controls;

namespace SiNet.App.Wpf.Admin.Security;

/// <summary>Two-way bindable PasswordBox helper for secret entry rows.</summary>
public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBinding));

    public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box || (bool)box.GetValue(IsUpdatingProperty))
        {
            return;
        }

        box.PasswordChanged -= BoxOnPasswordChanged;
        box.Password = e.NewValue as string ?? string.Empty;
        box.PasswordChanged += BoxOnPasswordChanged;
    }

    private static void BoxOnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box)
        {
            return;
        }

        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }

    public static void EnableBinding(PasswordBox box)
    {
        box.PasswordChanged += BoxOnPasswordChanged;
    }
}
