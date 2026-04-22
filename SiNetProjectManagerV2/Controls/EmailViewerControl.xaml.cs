using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using SiNetSQL.Models;
using SiNetSQL.MVVM.Components;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Reusable email viewer control — displays header, attachments bar, and body (WebView2).
/// Bind the <see cref="ViewModel"/> dependency property from the parent.
/// Optionally bind <see cref="TagTargets"/> to enable inline tag selector on attachments.
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

    public static readonly DependencyProperty TagTargetsProperty = DependencyProperty.Register(
        nameof(TagTargets),
        typeof(ObservableCollection<ProjectFile>),
        typeof(EmailViewerControl),
        new PropertyMetadata(null));

    /// <summary>
    /// Available ProjectFile targets for inline tag selector on attachments.
    /// When null, tag selector is hidden.
    /// </summary>
    public ObservableCollection<ProjectFile>? TagTargets
    {
        get => (ObservableCollection<ProjectFile>?)GetValue(TagTargetsProperty);
        set => SetValue(TagTargetsProperty, value);
    }

    /// <summary>
    /// Exposes the internal WebView2 control for scenarios where the parent needs direct access
    /// (e.g., OAuth session injection, PDF rendering, cleanup).
    /// </summary>
    public WebView2 WebView => EmailWebView;

    #region Events

    /// <summary>Raised when user right-clicks → "פתח קובץ מקומי".</summary>
    public event EventHandler<EmailAttachment>? OpenLocalFileRequested;

    /// <summary>Raised when user right-clicks → "הצג ב-ACC".</summary>
    public event EventHandler<EmailAttachment>? ShowInAccRequested;

    #endregion

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

    private void OpenLocalFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: EmailAttachment attachment })
        {
            OpenLocalFileRequested?.Invoke(this, attachment);
        }
    }

    private void ShowInAcc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: EmailAttachment attachment })
        {
            if (ShowInAccRequested != null)
                ShowInAccRequested.Invoke(this, attachment);
            else
                ViewModel?.RaiseShowInAccRequested(attachment);
        }
    }
}
