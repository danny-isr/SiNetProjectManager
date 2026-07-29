using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNet.App.Wpf.WebViewHosting;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.ProjectWork;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Embedded ACC document viewer (multi-tab WebView2). Registered for StandaloneNew and V2 New System.
/// Shares <see cref="WebView2SharedEnvironment"/> so Autodesk SSO persists across tabs.
/// </summary>
public sealed class WebView2AccViewerHost : IAccViewerHost
{
    public const int DefaultMaxTabs = 10;

    private readonly ISystemSettingsQueryService _systemSettings;
    private readonly IAppLogger _logger;
    private readonly Dictionary<string, TabEntry> _tabs = new(StringComparer.Ordinal);

    private ContentControl? _host;
    private Grid? _root;
    private WrapPanel? _tabStrip;
    private Grid? _contentArea;
    private CoreWebView2Environment? _environment;
    private string? _activeKey;
    private int? _maxTabs;

    public WebView2AccViewerHost(ISystemSettingsQueryService systemSettings, IAppLogger logger)
    {
        _systemSettings = systemSettings ?? throw new ArgumentNullException(nameof(systemSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable => true;

    public int MaxTabs => _maxTabs ?? DefaultMaxTabs;

    public event Action<string>? TabClosed;

    /// <summary>Resolves configured tab limit; non-positive values fall back to <see cref="DefaultMaxTabs"/>.</summary>
    public static int ResolveMaxTabs(int configured) => configured > 0 ? configured : DefaultMaxTabs;

    public void AttachHost(object hostElement)
    {
        if (hostElement is not ContentControl host)
            return;

        EnsureUiBuilt();

        if (_host is not null && !ReferenceEquals(_host, host))
            _host.Content = null;

        _host = host;
        host.Content = _root;
    }

    public bool IsTabOpen(string tabKey)
        => !string.IsNullOrEmpty(tabKey) && _tabs.ContainsKey(tabKey);

    public async Task<bool> OpenOrActivateTabAsync(AccViewerTabRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ViewerUrl) || string.IsNullOrWhiteSpace(request.TabKey))
            return false;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return await dispatcher.InvokeAsync(() => OpenOrActivateTabAsync(request, cancellationToken)).Task.Unwrap()
                .ConfigureAwait(true);

        await EnsureMaxTabsAsync(cancellationToken).ConfigureAwait(true);

        EnsureUiBuilt();
        if (_contentArea is null || _tabStrip is null)
            return false;

        if (_tabs.ContainsKey(request.TabKey))
        {
            Activate(request.TabKey);
            return true;
        }

        if (_tabs.Count >= MaxTabs)
        {
            _logger.Warn($"[ProjectWork.AccViewer] tab limit reached MaxTabs={MaxTabs}");
            return false;
        }

        var web = new Microsoft.Web.WebView2.Wpf.WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.White,
            Visibility = Visibility.Collapsed,
        };
        _contentArea.Children.Add(web);

        var tabButton = BuildTabButton(request);
        _tabStrip.Children.Add(tabButton);

        _tabs[request.TabKey] = new TabEntry(tabButton, web);
        Activate(request.TabKey);

        try
        {
            _environment ??= await WebView2SharedEnvironment
                .CreateSharedAsync("ProjectWork.AccViewer", _logger, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            await web.EnsureCoreWebView2Async(_environment).ConfigureAwait(true);
            ConfigureAccBehavior(web);
            web.CoreWebView2.Navigate(request.ViewerUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[ProjectWork.AccViewer] open failed tabKey={request.TabKey}", ex);
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
        try
        {
            entry.Web.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warn($"[ProjectWork.AccViewer] dispose tab failed: {ex.Message}");
        }

        _tabs.Remove(tabKey);

        if (_activeKey == tabKey)
        {
            _activeKey = null;
            var next = _tabs.Keys.FirstOrDefault();
            if (next is not null)
                Activate(next);
        }

        TabClosed?.Invoke(tabKey);
    }

    public void Clear()
    {
        foreach (var key in _tabs.Keys.ToList())
            CloseTab(key);
        _activeKey = null;
    }

    private async Task EnsureMaxTabsAsync(CancellationToken cancellationToken)
    {
        if (_maxTabs.HasValue)
            return;

        try
        {
            var settings = await _systemSettings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(true);
            _maxTabs = ResolveMaxTabs(settings.EmailOffice.AccViewerMaxTabs);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[ProjectWork.AccViewer] MaxTabs settings load failed; using {DefaultMaxTabs}: {ex.Message}");
            _maxTabs = DefaultMaxTabs;
        }
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

    private static void ConfigureAccBehavior(Microsoft.Web.WebView2.Wpf.WebView2 web)
    {
        var core = web.CoreWebView2;
        if (core is null)
            return;

        core.Settings.UserAgent = WebView2SharedEnvironment.ChromeUserAgent;

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(e.Uri))
                core.Navigate(e.Uri);
        };
    }

    private readonly record struct TabEntry(Border TabButton, Microsoft.Web.WebView2.Wpf.WebView2 Web);
}
