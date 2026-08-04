using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Email.Detail;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Optional popup window wrapper around <see cref="EmailSurfaceView"/>. Production New System
/// navigation hosts the surface inside <c>NewShellWindow</c> via <see cref="IEmailSurfaceHost"/>;
/// this window remains for standalone / fallback opens.
/// </summary>
public partial class EmailWindowView : Window
{
    private Rect _restoreBounds;
    private bool _isCustomMaximized;

    /// <summary>Design/standalone constructor: shows the window with fake design-time data.</summary>
    public EmailWindowView()
        : this(new EmailWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied New System view model.</summary>
    public EmailWindowView(EmailWindowViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        ViewModel = viewModel;
        DataContext = viewModel;
        EmailSurfaceHost.DataContext = viewModel;
        UpdateMaximizeButtonGlyph();
    }

    /// <summary>The bound view model for the read-only Gmail slice.</summary>
    public EmailWindowViewModel ViewModel { get; }

    public void SetBodyRenderer(IEmailBodyRenderer? bodyRenderer) =>
        EmailSurfaceHost.SetBodyRenderer(bodyRenderer);

    /// <summary>
    /// Placeholder workflow-first entry point. A later slice will use this to open the window from a
    /// task; for now it only forwards the context to the view model, which records it without starting
    /// or mutating any workflow.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsUnderChromeButton(e.OriginalSource))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (_isCustomMaximized)
        {
            return;
        }

        DragMove();
    }

    private static bool IsUnderChromeButton(object? originalSource)
    {
        for (var current = originalSource as DependencyObject;
             current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Button)
            {
                return true;
            }
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void EmailWindowView_OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.TryBlockCloseForBackgroundWork(this))
        {
            e.Cancel = true;
        }
    }

    private void ToggleMaximize()
    {
        if (_isCustomMaximized)
        {
            RestoreFromCustomMaximize();
        }
        else
        {
            MaximizeToWorkArea();
        }
    }

    private void MaximizeToWorkArea()
    {
        _restoreBounds = new Rect(Left, Top, Width, Height);
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
        ContentBorder.Margin = new Thickness(0);
        ContentBorder.CornerRadius = new CornerRadius(0);
        _isCustomMaximized = true;
        ResizeMode = ResizeMode.NoResize;
        UpdateMaximizeButtonGlyph();
    }

    private void RestoreFromCustomMaximize()
    {
        Left = _restoreBounds.Left;
        Top = _restoreBounds.Top;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        ContentBorder.Margin = new Thickness(8);
        ContentBorder.CornerRadius = new CornerRadius(8);
        _isCustomMaximized = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        UpdateMaximizeButtonGlyph();
    }

    private void UpdateMaximizeButtonGlyph()
    {
        if (MaximizeButton is null)
        {
            return;
        }

        MaximizeButton.Content = _isCustomMaximized ? "\u29C9" : "\u25A1";
        MaximizeButton.ToolTip = _isCustomMaximized ? "שחזר" : "הגדל";
    }
}
