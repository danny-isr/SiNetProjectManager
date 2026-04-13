using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Abstract base class for all floating tool windows.
/// Centralizes shared behavior: collapse/expand, drag, close, reset size,
/// opacity animation, position persistence, and settings listener.
/// Derived classes provide their specific ViewModel, body content, settings keys,
/// and domain-specific handlers.
/// </summary>
public abstract class FloatingWindowBase : Window
{
    private bool _isMouseOver;

    // Collapse/expand: store previous dimensions
    private double _expandedWidth;
    private double _expandedHeight;
    private double _expandedMinWidth;
    private double _expandedMinHeight;

    /// <summary>
    /// The ViewModel implementing <see cref="IFloatingWindowViewModel"/>.
    /// Derived class must return their specific ViewModel cast to the interface.
    /// </summary>
    protected abstract IFloatingWindowViewModel FloatingViewModel { get; }

    /// <summary>
    /// The XAML-named element used as the target for opacity animation.
    /// Typically the main <c>ContentBorder</c> wrapping the window content.
    /// </summary>
    protected abstract FrameworkElement OpacityTarget { get; }

    /// <summary>Default window width used by <see cref="ResetSizeButton_Click"/>.</summary>
    protected virtual double DefaultResetWidth => 420;

    /// <summary>Log prefix for debug messages (e.g., "[FloatingTasks]").</summary>
    protected abstract string LogPrefix { get; }

    /// <summary>Reads the saved window position from <see cref="AppSettings"/>.</summary>
    protected abstract (double Top, double Left, double Width, double Height)
        ReadWindowPosition(AppSettings settings);

    /// <summary>Writes the window position to <see cref="AppSettings"/>.</summary>
    protected abstract void WriteWindowPosition(
        AppSettings settings, double top, double left, double width, double height);

    /// <summary>
    /// Base constructor — subscribes to <c>Loaded</c> event for position restore.
    /// Derived constructors must call <see cref="InitializeFloatingBehavior"/> after
    /// <c>InitializeComponent()</c> and <c>DataContext</c> assignment.
    /// </summary>
    protected FloatingWindowBase()
    {
        Loaded += FloatingWindowBase_Loaded;
    }

    /// <summary>
    /// Must be called by derived constructors AFTER <c>InitializeComponent()</c>
    /// and <c>DataContext</c> assignment to wire shared behavior.
    /// </summary>
    protected void InitializeFloatingBehavior()
    {
        FloatingViewModel.PropertyChanged += ViewModel_PropertyChanged;

        var settings = App.AppSettings;
        if (settings != null)
        {
            FloatingViewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
            FloatingViewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
            settings.PropertyChanged += Settings_PropertyChanged;
        }

        OpacityTarget.Opacity = FloatingViewModel.IdleOpacity;
    }

    #region Window Lifecycle

    /// <summary>
    /// Restores saved window position on load.
    /// Falls back to <see cref="ApplyDefaultPosition"/> if no saved position or if saved bounds are off-screen.
    /// </summary>
    private void FloatingWindowBase_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        var (top, left, width, height) = ReadWindowPosition(settings);

        // Validate that we have a saved position (not NaN) and dimensions are reasonable
        if (!double.IsNaN(top) && !double.IsNaN(left) && width > 0 && height > 0)
        {
            // Ensure the window is at least partially visible on any monitor
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;

            if (left >= virtualLeft - width + 50 &&
                left <= virtualLeft + virtualWidth - 50 &&
                top >= virtualTop - height + 50 &&
                top <= virtualTop + virtualHeight - 50)
            {
                Top = top;
                Left = left;
                Width = width;
                Height = height;
                return;
            }
        }

        ApplyDefaultPosition();
    }

    /// <summary>
    /// Called when no saved position is available. Override to customize initial placement.
    /// Default: full-height, right-aligned on primary screen.
    /// </summary>
    protected virtual void ApplyDefaultPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Width = DefaultResetWidth;
        Height = workArea.Height;
        Top = workArea.Top;
        Left = workArea.Left + workArea.Width - Width;
    }

    /// <summary>Saves window position on closing.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPosition();
        base.OnClosing(e);
    }

    /// <summary>
    /// Unsubscribes from settings/VM events and disposes the ViewModel.
    /// Derived classes should override to do custom cleanup and call <c>base.OnClosed(e)</c>.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        var settings = App.AppSettings;
        if (settings != null)
            settings.PropertyChanged -= Settings_PropertyChanged;

        var vm = FloatingViewModel;
        if (vm != null)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.Dispose();
        }

        base.OnClosed(e);
    }

    #endregion

    #region Collapse / Expand

    /// <summary>Reacts to ViewModel property changes — handles collapse/expand transitions.</summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IFloatingWindowViewModel.IsCollapsed))
            return;

        if (FloatingViewModel.IsCollapsed)
            ApplyCollapsedState();
        else
            ApplyExpandedState();
    }

    /// <summary>
    /// Collapses the window to header-only: stores current dimensions, shrinks.
    /// Override <see cref="OnBodyCollapsed"/> to hide body elements not controlled by XAML bindings.
    /// </summary>
    private void ApplyCollapsedState()
    {
        _expandedWidth = Width;
        _expandedHeight = Height;
        _expandedMinWidth = MinWidth;
        _expandedMinHeight = MinHeight;

        OnBodyCollapsed();

        MinWidth = 200;
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        Width = Math.Min(Width, 260);
        ResizeMode = ResizeMode.NoResize;
    }

    /// <summary>
    /// Expands the window back to its previous full size.
    /// Override <see cref="OnBodyExpanded"/> to show body elements not controlled by XAML bindings.
    /// </summary>
    private void ApplyExpandedState()
    {
        OnBodyExpanded();

        SizeToContent = SizeToContent.Manual;
        MinWidth = _expandedMinWidth;
        MinHeight = _expandedMinHeight;
        Width = _expandedWidth;
        Height = _expandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
    }

    /// <summary>Called during collapse — override to hide body elements not controlled by XAML bindings.</summary>
    protected virtual void OnBodyCollapsed() { }

    /// <summary>Called during expand — override to show body elements not controlled by XAML bindings.</summary>
    protected virtual void OnBodyExpanded() { }

    #endregion

    #region Opacity Animation

    /// <summary>Fades to active opacity when the mouse enters the window.</summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isMouseOver = true;
        AnimateOpacity(FloatingViewModel.ActiveOpacity);
    }

    /// <summary>Fades to idle opacity when the mouse leaves the window.</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isMouseOver = false;
        AnimateOpacity(FloatingViewModel.IdleOpacity);
    }

    /// <summary>Smoothly animates the opacity to the target value over 0.3 seconds.</summary>
    private void AnimateOpacity(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        OpacityTarget.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Reacts to AppSettings changes (from SettingsWindow sliders) in real time.
    /// Override <see cref="OnSettingsChanged"/> to react to additional settings.
    /// </summary>
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Settings_PropertyChanged(sender, e));
            return;
        }

        var settings = App.AppSettings;
        if (settings == null) return;

        if (e.PropertyName is nameof(AppSettings.FloatingWindowActiveOpacity)
                           or nameof(AppSettings.FloatingWindowIdleOpacity))
        {
            FloatingViewModel.ActiveOpacity = settings.FloatingWindowActiveOpacity;
            FloatingViewModel.IdleOpacity = settings.FloatingWindowIdleOpacity;
            AnimateOpacity(_isMouseOver ? FloatingViewModel.ActiveOpacity : FloatingViewModel.IdleOpacity);
        }

        OnSettingsChanged(settings, e.PropertyName);
    }

    /// <summary>Virtual hook for derived windows to react to additional AppSettings changes.</summary>
    protected virtual void OnSettingsChanged(AppSettings settings, string? propertyName) { }

    #endregion

    #region Header Button Handlers

    /// <summary>Enables dragging the window from the custom header.</summary>
    protected void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    /// <summary>Closes the floating window via the custom close button.</summary>
    protected void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Resets the window to its default dimensions: narrow width, full screen height.
    /// Also restores from Maximized state if needed.
    /// </summary>
    protected void ResetSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        var workArea = SystemParameters.WorkArea;
        Width = DefaultResetWidth;
        Height = workArea.Height;
        Top = workArea.Top;
        Left = workArea.Left + workArea.Width - Width;
    }

    #endregion

    #region Position Persistence

    /// <summary>Persists current window bounds to AppSettings via SettingsManager.</summary>
    private void SaveWindowPosition()
    {
        var settings = App.AppSettings;
        if (settings == null)
            return;

        if (WindowState == WindowState.Normal && !FloatingViewModel.IsCollapsed)
        {
            WriteWindowPosition(settings, Top, Left, Width, Height);
        }
        else if (FloatingViewModel.IsCollapsed)
        {
            WriteWindowPosition(settings, Top, Left,
                _expandedWidth > 0 ? _expandedWidth : Width,
                _expandedHeight > 0 ? _expandedHeight : Height);
        }

        try
        {
            SettingsManager.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{LogPrefix} Failed to save window position: {ex.Message}");
        }
    }

    #endregion
}
