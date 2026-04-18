using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using SiNetSQL.MVVM.Components;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Reusable email viewer control — displays header, attachments bar, and body (WebView2).
/// Bind the <see cref="ViewModel"/> dependency property from the parent.
/// </summary>
public partial class EmailViewerControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(EmailViewerViewModel),
        typeof(EmailViewerControl),
        new PropertyMetadata(null));

    public EmailViewerViewModel? ViewModel
    {
        get => (EmailViewerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Exposes the internal WebView2 control for scenarios where the parent needs direct access
    /// (e.g., OAuth session injection, PDF rendering, cleanup).
    /// </summary>
    public WebView2 WebView => EmailWebView;

    public EmailViewerControl()
    {
        InitializeComponent();
    }

    private void AttachmentBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EmailAttachment attachment } && ViewModel != null)
        {
            ViewModel.RaiseAttachmentClicked(attachment);
        }
    }
}
