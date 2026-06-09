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
        new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmailViewerControl control)
        {
            if (e.OldValue is EmailViewerViewModel oldVm)
            {
                oldVm.PropertyChanged -= control.Vm_PropertyChanged;
            }
            if (e.NewValue is EmailViewerViewModel newVm)
            {
                newVm.PropertyChanged += control.Vm_PropertyChanged;
            }
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmailViewerViewModel.Email))
        {
            if (EncodingComboBox != null)
            {
                EncodingComboBox.SelectionChanged -= EncodingComboBox_SelectionChanged;
                EncodingComboBox.SelectedIndex = 0;
                EncodingComboBox.SelectionChanged += EncodingComboBox_SelectionChanged;
            }
        }
    }

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

    public static readonly DependencyProperty ForceHtmlOnlyProperty = DependencyProperty.Register(
        nameof(ForceHtmlOnly),
        typeof(bool),
        typeof(EmailViewerControl),
        new PropertyMetadata(false));

    /// <summary>
    /// When true, the viewer bypasses URL navigation to Gmail and directly displays the static HTML body.
    /// </summary>
    public bool ForceHtmlOnly
    {
        get => (bool)GetValue(ForceHtmlOnlyProperty);
        set => SetValue(ForceHtmlOnlyProperty, value);
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

    private void EncodingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel?.Email == null) return;
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            string encoding = selectedItem.Tag?.ToString() ?? "utf-8";
            ReloadWithEncoding(encoding);
        }
    }

    public void ReloadWithEncoding(string encoding)
    {
        if (ViewModel?.Email == null) return;
        
        string htmlContent = ViewModel.Email.HtmlBodyForDisplay;
        if (string.IsNullOrEmpty(htmlContent)) return;

        // Try to replace existing meta charset
        htmlContent = System.Text.RegularExpressions.Regex.Replace(
            htmlContent,
            @"<meta\s+charset=[""'][^""']+[""']\s*/?>",
            $"<meta charset=\"{encoding}\">",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!htmlContent.Contains($"charset=\"{encoding}\"", StringComparison.OrdinalIgnoreCase))
        {
            htmlContent = $"<meta charset=\"{encoding}\">" + htmlContent;
        }

        EmailWebView.NavigateToString(htmlContent);
    }
}
