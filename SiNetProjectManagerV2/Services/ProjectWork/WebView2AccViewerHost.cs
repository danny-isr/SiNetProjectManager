using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNet.Application.ProjectWork;
using SiNetProjectManagerV2.WPFUserControl;

namespace SiNetProjectManagerV2.Services.ProjectWork;

/// <summary>
/// V2 host adapter for the embedded ACC document viewer. Mirrors <c>WebView2EmailBodyRenderer</c>: the
/// WebView2/tab UI lives here in the host (which ships WebView2), while the clean ProjectWork surface
/// depends only on <see cref="IAccViewerHost"/>. Manages a strip of tabs, one persistent WebView2 per
/// tab keyed by <see cref="AccViewerTabRequest.TabKey"/>, sharing the app-wide WebView2 profile so ACC
/// / Autodesk SSO is reused. Reproduces the legacy multi-tab ACC viewer behaviour from
/// <c>ProjectWorkViewModel</c>.
/// </summary>
internal sealed class WebView2AccViewerHost : IAccViewerHost
{
    private const int DefaultMaxTabs = 10;

    private readonly Dictionary<string, TabEntry> _tabs = new(StringComparer.Ordinal);

    private ContentControl? _host;
    private Grid? _root;
    private WrapPanel? _tabStrip;
    private Grid? _contentArea;
    private CoreWebView2Environment? _environment;
    private string? _activeKey;

    public bool IsAvailable => true;

    public int MaxTabs => DefaultMaxTabs;

    public void AttachHost(object hostElement)
    {
        if (hostElement is not ContentControl host)
            return;

        EnsureUiBuilt();

        // A visual can only have one parent: detach from the previous host before re-parenting.
        if (_host is not null && !ReferenceEquals(_host, host))
            _host.Content = null;

        _host = host;
        host.Content = _root;
    }

    public async Task<bool> OpenOrActivateTabAsync(AccViewerTabRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ViewerUrl) || string.IsNullOrWhiteSpace(request.TabKey))
            return false;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return await dispatcher.InvokeAsync(() => OpenOrActivateTabAsync(request, cancellationToken)).Task.Unwrap().ConfigureAwait(true);

        EnsureUiBuilt();
        if (_contentArea is null || _tabStrip is null)
            return false;

        if (_tabs.ContainsKey(request.TabKey))
        {
            Activate(request.TabKey);
            return true;
        }

        if (_tabs.Count >= MaxTabs)
            return false;

        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White, Visibility = Visibility.Collapsed };
        _contentArea.Children.Add(web);

        var tabButton = BuildTabButton(request);
        _tabStrip.Children.Add(tabButton);

        _tabs[request.TabKey] = new TabEntry(tabButton, web);
        Activate(request.TabKey);

        try
        {
            _environment ??= await WebView2Helper.CreateSharedEnvironmentAsync("ProjectWork.AccViewer").ConfigureAwait(true);
            await web.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
            ConfigureAccBehavior(web);
            web.CoreWebView2.Navigate(request.ViewerUrl);
            return true;
        }
        catch
        {
            CloseTab(request.TabKey);
            return false;
        }
    }

    public void CloseTab(string tabKey)
    {
        if (string.IsNullOrEmpty(tabKey) || !_tabs.TryGetValue(tabKey, out var entry))
            return;

        _tabStrip?.Children.Remove(entry.TabButton);
        _contentArea?.Children.Remove(entry.Web);
        try { entry.Web.Dispose(); } catch { /* already disposed */ }
        _tabs.Remove(tabKey);

        if (_activeKey == tabKey)
        {
            _activeKey = null;
            var next = _tabs.Keys.FirstOrDefault();
            if (next is not null)
                Activate(next);
        }
    }

    public void Clear()
    {
        foreach (var key in _tabs.Keys.ToList())
            CloseTab(key);
        _activeKey = null;
    }

    private void EnsureUiBuilt()
    {
        if (_root is not null)
            return;

        _tabStrip = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 0) };
        _contentArea = new Grid();

        var tabStripBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            Child = _tabStrip,
        };

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(tabStripBorder, 0);
        Grid.SetRow(_contentArea, 1);
        _root.Children.Add(tabStripBorder);
        _root.Children.Add(_contentArea);
    }

    private Border BuildTabButton(AccViewerTabRequest request)
    {
        var title = new Button
        {
            Content = request.Title,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3, 6, 3),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = request.ViewerUrl,
        };
        title.Click += (_, _) => Activate(request.TabKey);

        var close = new Button
        {
            Content = "\u00D7",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
        };
        close.Click += (_, _) => CloseTab(request.TabKey);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(title);
        panel.Children.Add(close);

        return new Border
        {
            Margin = new Thickness(2, 2, 2, 0),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Background = Brushes.White,
            Child = panel,
        };
    }

    private void Activate(string tabKey)
    {
        _activeKey = tabKey;
        foreach (var (key, entry) in _tabs)
        {
            var isActive = key == tabKey;
            entry.Web.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            entry.TabButton.Background = isActive
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
        }
    }

    private static void ConfigureAccBehavior(WebView2 web)
    {
        var core = web.CoreWebView2;
        if (core is null)
            return;

        core.Settings.UserAgent = WebView2Helper.ChromeUserAgent;

        // ACC viewer mode: keep new-window requests in-place (mirrors legacy IsAccViewer behaviour).
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(e.Uri))
                core.Navigate(e.Uri);
        };
    }

    private readonly record struct TabEntry(Border TabButton, WebView2 Web);
}
