using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNetProjectManagerV2.Dialogs;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;
using SiOffice.GoogleConnector;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace SiNetProjectManagerV2.WPFUserControl
{
    /// <summary>
    /// Attached property for binding HTML content to WebView2 control.
    /// Handles initialization and navigation to HTML strings with proper async safety.
    /// 
    /// Key features:
    /// - Ensures CoreWebView2 is initialized before navigation
    /// - Handles race conditions during initialization
    /// - Serves inline images via virtual host to avoid Base64 size limits
    /// - Thread-safe initialization tracking
    /// </summary>
    public static class WebView2Helper
    {
        // Track initialization state per WebView2 instance to prevent race conditions
        private static readonly ConcurrentDictionary<WebView2, WebView2State> _webViewStates = new();

        // Illegal characters for file system paths
        private static readonly Regex _illegalPathChars = new(
            @"[<>:""/\\|?*\x00-\x1F]",
            RegexOptions.Compiled);

        /// <summary>
        /// The email address of the currently authenticated Google user.
        /// Set this BEFORE the WebView2 control initializes to ensure the correct
        /// persistent UserDataFolder is selected for SSO session persistence.
        /// </summary>
        public static string? CurrentUserEmail { get; set; }

        /// <summary>
        /// Raised on the background thread when a project-associated download completes.
        /// Parameters: local file path, sanitized file name, associated <see cref="EmailInfo"/>.
        /// </summary>
        internal static event Action<string, string, EmailInfo>? ProjectFileDownloaded;

        // Modern Chrome User-Agent to prevent Google from triggering embedded-browser security redirects
        internal const string ChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        /// <summary>
        /// Persistent JS cleaner injected via <see cref="CoreWebView2.DOMContentLoaded"/>.
        /// Uses a whitelist "content isolation" approach: locates the email message body
        /// (<c>.ii.gt</c> or <c>.a3s</c>), promotes its nearest stable ancestor to a
        /// fixed full-viewport overlay, and hides all other top-level siblings.
        /// A <c>MutationObserver</c> re-fires on every DOM mutation so the isolation
        /// survives Gmail SPA re-renders and thread switches.
        /// </summary>
        private const string GmailCleanViewJs = @"
(function() {
    var isolateContent = function() {
        var emailBody = document.querySelector('.ii.gt') || document.querySelector('.a3s');
        if (!emailBody) return;
        var container = emailBody.closest('.nH.bkK') || emailBody;
        document.body.childNodes.forEach(function(node) {
            if (node.nodeType === 1 && !node.contains(container)) {
                node.style.display = 'none';
            }
        });
        container.style.position = 'fixed';
        container.style.top = '0';
        container.style.left = '0';
        container.style.width = '100vw';
        container.style.height = '100vh';
        container.style.backgroundColor = 'white';
        container.style.zIndex = '9999';
        container.style.overflow = 'auto';
        container.style.padding = '20px';
    };
    isolateContent();
    if (document.body) {
        new MutationObserver(isolateContent).observe(document.body, { childList: true, subtree: true });
    }
})();";

        /// <summary>
        /// Google Calendar day-view URL used for the sidebar WebView2.
        /// The <c>hl=iw</c> parameter sets the UI language to Hebrew.
        /// Content isolation is handled by <see cref="CalendarCleanViewJs"/>.
        /// </summary>
        public static readonly string CalendarDayViewUrl = "https://calendar.google.com/calendar/u/0/r/day";

        /// <summary>
        /// JavaScript that isolates the Google Calendar container by hiding all sibling
        /// elements, clearing restrictive parent styles, and expanding the container to
        /// fill the viewport.
        /// Uses a prioritised lookup: <c>main#YPCqFe</c> (stable element ID from DevTools)
        /// → <c>.WN9Ejb.buw</c> → <c>.nH</c> as fallbacks.
        /// A <see cref="MutationObserver"/> re-applies the isolation whenever Google
        /// dynamically rebuilds the DOM.
        /// Additionally intercepts ALL link clicks and URL changes to open external URLs in popup windows.
        /// </summary>
        private const string CalendarCleanViewJs = @"
(function() {
    const log = (msg) => {
        try {
            window.chrome.webview.postMessage('[Calendar JS] ' + msg);
        } catch(e) {
            console.log('[Calendar JS] ' + msg);
        }
    };

    log('Script loaded and executing...');
    log('Current URL: ' + window.location.href);

    const isolateCalendar = () => {
        const mainCalendar = document.getElementById('YPCqFe')
            || document.querySelector('.WN9Ejb.buw')
            || document.querySelector('.nH');
        if (mainCalendar) {
            document.body.childNodes.forEach(node => {
                if (node.nodeType === 1 && !node.contains(mainCalendar)) {
                    node.style.setProperty('display', 'none', 'important');
                }
            });
            let p = mainCalendar.parentElement;
            while (p && p !== document.body) {
                p.style.setProperty('margin', '0', 'important');
                p.style.setProperty('padding', '0', 'important');
                p.style.setProperty('height', '100%', 'important');
                p = p.parentElement;
            }
            mainCalendar.style.setProperty('position', 'fixed', 'important');
            mainCalendar.style.setProperty('top', '0', 'important');
            mainCalendar.style.setProperty('left', '0', 'important');
            mainCalendar.style.setProperty('width', '100vw', 'important');
            mainCalendar.style.setProperty('height', '100vh', 'important');
            mainCalendar.style.setProperty('z-index', '9999', 'important');
            mainCalendar.style.setProperty('background-color', 'white', 'important');
        }
    };

    // Monitor URL changes (for SPA navigation)
    let lastUrl = window.location.href;
    const checkUrlChange = () => {
        const currentUrl = window.location.href;
        if (currentUrl !== lastUrl) {
            log('🔄 URL changed from: ' + lastUrl);
            log('🔄 URL changed to: ' + currentUrl);

            // Check if navigated to event detail or external link
            if (!currentUrl.includes('/r/day') && 
                !currentUrl.includes('/r/week') && 
                !currentUrl.includes('/r/month') && 
                !currentUrl.includes('/r/agenda')) {
                log('🚨 Non-calendar-view URL detected (event details or external)');
            }

            lastUrl = currentUrl;
        }
    };

    // Monitor URL changes via interval (fallback)
    setInterval(checkUrlChange, 500);

    // Intercept history.pushState and history.replaceState
    const originalPushState = history.pushState;
    const originalReplaceState = history.replaceState;

    history.pushState = function(state, title, url) {
        log('📍 history.pushState intercepted. URL: ' + url);

        // Check if this is an event edit navigation
        if (url && (url.includes('/r/eventedit') || url.includes('eventedit'))) {
            log('🚨 EVENT EDIT detected in pushState - BLOCKING and opening in external window');

            // Build full URL if it's relative
            const fullUrl = url.startsWith('http') ? url : 'https://calendar.google.com' + url;
            log('Opening external window with URL: ' + fullUrl);

            // Open in external window instead
            window.open(fullUrl, '_blank');

            // DON'T call original pushState - prevent navigation
            return;
        }

        log('✓ Allowing pushState for: ' + url);
        originalPushState.apply(this, arguments);
        checkUrlChange();
    };

    history.replaceState = function(state, title, url) {
        log('📍 history.replaceState intercepted. URL: ' + url);

        // Check if this is an event edit navigation
        if (url && (url.includes('/r/eventedit') || url.includes('eventedit'))) {
            log('🚨 EVENT EDIT detected in replaceState - BLOCKING and opening in external window');

            // Build full URL if it's relative
            const fullUrl = url.startsWith('http') ? url : 'https://calendar.google.com' + url;
            log('Opening external window with URL: ' + fullUrl);

            // Open in external window instead
            window.open(fullUrl, '_blank');

            // DON'T call original replaceState - prevent navigation
            return;
        }

        log('✓ Allowing replaceState for: ' + url);
        originalReplaceState.apply(this, arguments);
        checkUrlChange();
    };

    // Intercept ALL clicks on the page - using multiple event types
    const handleClick = (e) => {
        const tagName = e.target.tagName || 'unknown';
        const className = e.target.className || '';
        log('Click on: ' + tagName + ' class=' + className);

        // Find if the clicked element or any parent is a link
        let target = e.target;
        let depth = 0;
        while (target && depth < 10) {
            if (target.tagName === 'A' && target.href) {
                const href = target.href;
                log('Found A tag! href: ' + href);

                // Check if it's a calendar internal navigation (only day/week/month/agenda views)
                // Event details (/r/eventedit) should open in external window
                if (href.includes('calendar.google.com/calendar/u/') && 
                    (href.includes('/r/day') || 
                     href.includes('/r/week') || 
                     href.includes('/r/month') || 
                     href.includes('/r/agenda'))) {
                    log('Internal calendar view navigation - allowing');
                    return;
                }

                // External link or event details - open in new window
                log('🚀 EXTERNAL LINK or EVENT DETAILS - blocking and opening in new window: ' + href);
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                window.open(href, '_blank');
                return false;
            }

            target = target.parentElement;
            depth++;
        }
    };

    // Install event listeners
    document.addEventListener('click', handleClick, true);
    document.addEventListener('mousedown', handleClick, true);

    log('✓ Click interceptors installed');
    log('✓ URL change monitoring installed');

    isolateCalendar();
    new MutationObserver(isolateCalendar).observe(document.body, { childList: true, subtree: true });
    log('✓ Calendar isolation active');
    log('═══ Calendar script ready ═══');
})();";

        private class WebView2State : IDisposable
        {
            public bool IsInitialized { get; set; }
            public bool VirtualHostConfigured { get; set; }
            public bool BrowserConfigured { get; set; }
            public string? PendingHtml { get; set; }
            public string? PendingUrl { get; set; }
            public string? FallbackHtml { get; set; }
            public long NavigationGeneration { get; set; }
            public CancellationTokenSource? FallbackDelayCts { get; set; }
            public readonly SemaphoreSlim InitSemaphore = new(1, 1);
            public readonly object Lock = new();

            public void Dispose()
            {
                FallbackDelayCts?.Cancel();
                FallbackDelayCts?.Dispose();
                InitSemaphore.Dispose();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ATTACHED PROPERTY: HtmlSource (legacy HTML string rendering)
        // ══════════════════════════════════════════════════════════════════

        public static readonly DependencyProperty HtmlSourceProperty =
            DependencyProperty.RegisterAttached(
                "HtmlSource",
                typeof(string),
                typeof(WebView2Helper),
                new PropertyMetadata(null, OnHtmlSourceChanged));

        public static string GetHtmlSource(DependencyObject obj)
        {
            return (string)obj.GetValue(HtmlSourceProperty);
        }

        public static void SetHtmlSource(DependencyObject obj, string value)
        {
            obj.SetValue(HtmlSourceProperty, value);
        }

        // ══════════════════════════════════════════════════════════════════
        // ATTACHED PROPERTY: NavigateUrl (Phase 2 — Gmail popout URL)
        // Navigates WebView2 to a URL. Falls back to FallbackHtml on failure.
        // ══════════════════════════════════════════════════════════════════

        public static readonly DependencyProperty NavigateUrlProperty =
            DependencyProperty.RegisterAttached(
                "NavigateUrl",
                typeof(string),
                typeof(WebView2Helper),
                new PropertyMetadata(null, OnNavigateUrlChanged));

        public static string GetNavigateUrl(DependencyObject obj)
        {
            return (string)obj.GetValue(NavigateUrlProperty);
        }

        public static void SetNavigateUrl(DependencyObject obj, string value)
        {
            obj.SetValue(NavigateUrlProperty, value);
        }

        // ══════════════════════════════════════════════════════════════════
        // ATTACHED PROPERTY: IsAccViewer (marks WebView2 as ACC document viewer)
        // Disables Gmail navigation guard and keeps new-window requests in-place.
        // ══════════════════════════════════════════════════════════════════

        public static readonly DependencyProperty IsAccViewerProperty =
            DependencyProperty.RegisterAttached(
                "IsAccViewer",
                typeof(bool),
                typeof(WebView2Helper),
                new PropertyMetadata(false));

        public static bool GetIsAccViewer(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsAccViewerProperty);
        }

        public static void SetIsAccViewer(DependencyObject obj, bool value)
        {
            obj.SetValue(IsAccViewerProperty, value);
        }

        public static readonly DependencyProperty FallbackHtmlProperty =
            DependencyProperty.RegisterAttached(
                "FallbackHtml",
                typeof(string),
                typeof(WebView2Helper),
                new PropertyMetadata(null));

        public static string GetFallbackHtml(DependencyObject obj)
        {
            return (string)obj.GetValue(FallbackHtmlProperty);
        }

        public static void SetFallbackHtml(DependencyObject obj, string value)
        {
            obj.SetValue(FallbackHtmlProperty, value);
        }

        // ══════════════════════════════════════════════════════════════════
        // ATTACHED PROPERTY: SelectedEmailInfo (Phase 3 — download path resolution)
        // Provides the currently selected EmailInfo to the download interception handler.
        // ══════════════════════════════════════════════════════════════════

        public static readonly DependencyProperty SelectedEmailInfoProperty =
            DependencyProperty.RegisterAttached(
                "SelectedEmailInfo",
                typeof(EmailInfo),
                typeof(WebView2Helper),
                new PropertyMetadata(null));

        public static EmailInfo? GetSelectedEmailInfo(DependencyObject obj)
        {
            return (EmailInfo?)obj.GetValue(SelectedEmailInfoProperty);
        }

        public static void SetSelectedEmailInfo(DependencyObject obj, EmailInfo? value)
        {
            obj.SetValue(SelectedEmailInfoProperty, value);
        }

        private static async void OnHtmlSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not WebView2 webView) return;

            var htmlContent = e.NewValue as string;
            var state = _webViewStates.GetOrAdd(webView, _ => new WebView2State());

            // Store desired content — the SemaphoreSlim in InitializeAndNavigateAsync
            // guarantees only one init runs; late arrivals see IsInitialized = true.
            state.PendingHtml = htmlContent;

            // Fast path: already initialized
            if (state.IsInitialized && webView.CoreWebView2 != null)
            {
                NavigateToHtmlSafely(webView, htmlContent);
                return;
            }

            await InitializeAndNavigateAsync(webView, state);
        }

        // ══════════════════════════════════════════════════════════════════
        // NavigateUrl change handler — URL navigation with fallback
        // ══════════════════════════════════════════════════════════════════

        private static async void OnNavigateUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not WebView2 webView) return;

            var url = e.NewValue as string;
            var state = _webViewStates.GetOrAdd(webView, _ => new WebView2State());

            // Store URL and fallback — URL takes priority over HTML
            state.PendingUrl = url;
            state.PendingHtml = null;
            state.FallbackHtml = GetFallbackHtml(webView);

            // Fast path: already initialized
            if (state.IsInitialized && webView.CoreWebView2 != null)
            {
                NavigateToUrlSafely(webView, state, url);
                return;
            }

            await InitializeAndNavigateAsync(webView, state);
        }

        private static async Task InitializeAndNavigateAsync(WebView2 webView, WebView2State state)
        {
            // Fast path: already initialized
            if (state.IsInitialized)
                return;

            // Atomic gate — only the first caller proceeds; duplicates exit immediately.
            // SemaphoreSlim(1,1) replaces the previous lock+bool pattern that allowed
            // double initialization when both property handlers fired concurrently.
            if (!await state.InitSemaphore.WaitAsync(0))
            {
                System.Diagnostics.Debug.WriteLine("WebView2: Init already in progress (semaphore held), skipping duplicate");
                return;
            }

            try
            {
                // Double-check after acquiring semaphore
                if (state.IsInitialized)
                    return;

                System.Diagnostics.Debug.WriteLine("WebView2: Starting initialization...");

                // Ensure we're on the UI thread for WebView2 initialization.
                // Double-await: outer await waits for dispatcher invocation,
                // inner await waits for the actual async init work to complete.
                if (!webView.Dispatcher.CheckAccess())
                {
                    await await webView.Dispatcher.InvokeAsync(
                        () => InitializeCoreWebView2Async(webView, state));
                }
                else
                {
                    await InitializeCoreWebView2Async(webView, state);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Initialization failed - {ex.Message}");
                state.IsInitialized = false;
            }
            finally
            {
                state.InitSemaphore.Release();
            }
        }

        private static async Task InitializeCoreWebView2Async(WebView2 webView, WebView2State state)
        {
            try
            {
                // Check if already initialized (can happen in race condition)
                if (webView.CoreWebView2 == null)
                {
                    bool isAcc = GetIsAccViewer(webView);
                    var environment = isAcc
                        ? await CreateAccEnvironmentAsync()
                        : await CreateUserEnvironmentAsync();
                    await webView.EnsureCoreWebView2Async(environment);
                    System.Diagnostics.Debug.WriteLine($"WebView2: Core initialization completed (ACC={isAcc})");
                }

                // Phase 1.1: One-time browser configuration (UA masking, popup interception)
                if (!state.BrowserConfigured && webView.CoreWebView2 != null)
                {
                    ConfigureBrowserBehavior(webView);
                    state.BrowserConfigured = true;
                }

                // Configure virtual host for inline images (only once per instance, Gmail only)
                if (!state.VirtualHostConfigured && webView.CoreWebView2 != null && !GetIsAccViewer(webView))
                {
                    ConfigureVirtualHostForImages(webView.CoreWebView2);
                    state.VirtualHostConfigured = true;
                }

                state.IsInitialized = true;
                System.Diagnostics.Debug.WriteLine("WebView2: Initialization completed successfully");

                // Dispatch to pending URL or HTML content (read latest values)
                var pendingUrl = state.PendingUrl;
                var pendingHtml = state.PendingHtml;

                if (!string.IsNullOrEmpty(pendingUrl))
                {
                    NavigateToUrlSafely(webView, state, pendingUrl);
                }
                else
                {
                    NavigateToHtmlSafely(webView, pendingHtml);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: CoreWebView2 initialization error - {ex.Message}");
                state.IsInitialized = false;

                // Try to show an error message in the control if possible
                try
                {
                    if (webView.CoreWebView2 != null)
                    {
                        webView.NavigateToString(GetErrorHtml("WebView2 initialization failed"));
                    }
                }
                catch
                {
                    // Silently fail - nothing more we can do
                }
            }
        }

        /// <summary>
        /// Enables mouse-wheel scrolling inside WebView2.
        /// WPF routes wheel events to the focused element; WebView2 doesn't auto-focus
        /// on hover, so we give it keyboard focus when the mouse enters.
        /// </summary>
        internal static void EnableScrollWheelFocus(WebView2 webView)
        {
            webView.MouseEnter += (_, _) =>
            {
                if (!webView.IsFocused)
                    webView.Focus();
            };
        }

        /// <summary>
        /// Configures WebView2 browser behavior for Google authentication, navigation guard, and download interception.
        /// 1. User-Agent masking: Prevents Google "Embedded Browser" security redirects.
        /// 2. NewWindowRequested: ALL new-window requests open in a floating browser window.
        /// 3. NavigationStarting: Only allows Gmail core domains; everything else opens in a floating browser window.
        /// 4. DownloadStarting: Intercepts Gmail attachment downloads, saves to ACC-mirrored path.
        /// 5. DOMContentLoaded: Injects clean-view scripts for Gmail and Calendar.
        /// </summary>
        private static void ConfigureBrowserBehavior(WebView2 webView)
        {
            var coreWebView = webView.CoreWebView2;
            bool isAccViewer = GetIsAccViewer(webView);

            // 0. Enable scroll wheel by auto-focusing on mouse enter
            EnableScrollWheelFocus(webView);

            // 1. Spoof User-Agent to look like standalone Chrome
            coreWebView.Settings.UserAgent = ChromeUserAgent;
            System.Diagnostics.Debug.WriteLine("WebView2: User-Agent set to modern Chrome (prevents embedded-browser redirect)");

            if (isAccViewer)
            {
                // ACC viewer mode: allow all navigations, keep new-window requests in-place
                coreWebView.NewWindowRequested += (sender, e) =>
                {
                    e.Handled = true;
                    if (!string.IsNullOrEmpty(e.Uri))
                    {
                        System.Diagnostics.Debug.WriteLine($"WebView2 [ACC]: New-window → navigating in-place: {e.Uri}");
                        coreWebView.Navigate(e.Uri);
                    }
                };

                System.Diagnostics.Debug.WriteLine("WebView2: ACC viewer mode configured (UA + in-place navigation)");
                return;
            }

            // 2. Intercept new-window requests — ALL open in floating browser window
            //    NewWindowRequested fires for target="_blank" / window.open() — always external.
            coreWebView.NewWindowRequested += (sender, e) =>
            {
                e.Handled = true;
                AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                AppLogger.Info($"WebView2 [NEW-WINDOW]: NewWindowRequested event fired");
                AppLogger.Info($"WebView2 [NEW-WINDOW]: Target URI → {e.Uri}");
                AppLogger.Info($"WebView2 [NEW-WINDOW]: Opening in external browser window");
                AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                if (!string.IsNullOrEmpty(e.Uri))
                {
                    var emailInfo = GetSelectedEmailInfo(webView);
                    webView.Dispatcher.InvokeAsync(() => OpenExternalBrowserWindow(webView, e.Uri, emailInfo));
                }
            };

            // 3. Navigation guard — only allow Gmail core domains; everything else opens externally
            coreWebView.NavigationStarting += (sender, e) =>
            {
                AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                AppLogger.Info($"WebView2 [NAVIGATION]: NavigationStarting event fired");
                AppLogger.Info($"WebView2 [NAVIGATION]: Target URI → {e.Uri}");
                AppLogger.Info($"WebView2 [NAVIGATION]: IsUserInitiated → {e.IsUserInitiated}");
                AppLogger.Info($"WebView2 [NAVIGATION]: IsRedirected → {e.IsRedirected}");

                if (string.IsNullOrEmpty(e.Uri))
                {
                    AppLogger.Info($"WebView2 [NAVIGATION]: URI is empty, allowing navigation");
                    AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    return;
                }

                // Always allow internal URIs and virtual host
                if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                    e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    e.Uri.Contains(InlineImageProvider.VirtualHostName, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info($"WebView2 [NAVIGATION]: Internal/Virtual URI detected, allowing navigation");
                    AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    return;
                }

                // Allow only Gmail core navigations (mail, auth, calendar) + supporting assets
                bool isCoreNav = IsGmailCoreNavigation(e.Uri);
                AppLogger.Info($"WebView2 [NAVIGATION]: IsGmailCoreNavigation result → {isCoreNav}");

                if (isCoreNav)
                {
                    AppLogger.Info($"WebView2 [NAVIGATION]: ✓ ALLOWED - Core Gmail/Calendar navigation");
                    AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    return;
                }

                // Everything else (other Google domains, external sites) → floating browser window
                e.Cancel = true;
                AppLogger.Info($"WebView2 [NAVIGATION]: ✗ BLOCKED - Opening in external browser window");
                AppLogger.Info($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                var emailInfo = GetSelectedEmailInfo(webView);
                webView.Dispatcher.InvokeAsync(() => OpenExternalBrowserWindow(webView, e.Uri, emailInfo));
            };

            // 4. Download interception — silent save to ACC-mirrored path for Gmail attachments
            coreWebView.DownloadStarting += (sender, e) =>
            {
                HandleDownloadStarting(webView, e);
            };

            // 5. DOMContentLoaded: inject persistent clean-view scripts.
            //    Fires on every navigation (SPA push-state included) so the
            //    MutationObserver is re-established whenever the page reloads.
            coreWebView.DOMContentLoaded += (sender, e) =>
            {
                if (sender is CoreWebView2 core)
                {
                    var uri = core.Source;
                    AppLogger.Info($"[DOMContentLoaded] Event fired for URI: {uri}");
                    if (!string.IsNullOrEmpty(uri))
                    {
                        if (uri.Contains("mail.google.com", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Info($"[DOMContentLoaded] Gmail detected, injecting clean-view script");
                            _ = InjectCleanViewAsync(core);
                        }
                        else if (uri.Contains("calendar.google.com", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Info($"[DOMContentLoaded] Calendar detected, injecting calendar clean-view script");
                            _ = InjectCalendarCleanViewAsync(core);
                        }
                        else
                        {
                            AppLogger.Info($"[DOMContentLoaded] Non-Gmail/Calendar page, skipping script injection");
                        }
                    }
                }
            };

            // 6. Console message handler: capture JavaScript console.log for debugging
            coreWebView.WebMessageReceived += (sender, e) =>
            {
                AppLogger.Info($"[JS → C#] {e.WebMessageAsJson}");
            };

            System.Diagnostics.Debug.WriteLine("WebView2: Browser behavior configured (UA + navigation guard + download interception + clean-view)");
        }

        /// <summary>
        /// Checks whether a URI is a core Gmail/Calendar navigation that must stay in the WebView2.
        /// Only allows: mail.google.com, accounts.google.com, and specific calendar view paths.
        /// Supporting asset domains (googleapis, gstatic, googleusercontent) are also allowed.
        /// Calendar event details (/r/eventedit) and external links are excluded and will open 
        /// in a floating browser window.
        /// </summary>
        private static bool IsGmailCoreNavigation(string uri)
        {
            AppLogger.Info($"    [IsGmailCoreNavigation] Checking URI: {uri}");

            // Supporting asset domains — CSS, JS, fonts, images loaded by Gmail/Calendar
            if (uri.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"    [IsGmailCoreNavigation] → TRUE (supporting asset domain)");
                return true;
            }

            // Core Google services that must navigate in-place
            if (uri.Contains("mail.google.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"    [IsGmailCoreNavigation] → TRUE (mail/accounts domain)");
                return true;
            }

            // Calendar: Only allow core view navigation paths (day, week, month, agenda)
            // Block event detail pages (/r/eventedit) and external links
            if (uri.Contains("calendar.google.com", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"    [IsGmailCoreNavigation] Calendar domain detected, checking paths...");

                bool hasCalendarPath = uri.Contains("/calendar/u/", StringComparison.OrdinalIgnoreCase);
                AppLogger.Info($"    [IsGmailCoreNavigation] Has /calendar/u/ path → {hasCalendarPath}");

                if (hasCalendarPath)
                {
                    bool isDayView = uri.Contains("/r/day", StringComparison.OrdinalIgnoreCase);
                    bool isWeekView = uri.Contains("/r/week", StringComparison.OrdinalIgnoreCase);
                    bool isMonthView = uri.Contains("/r/month", StringComparison.OrdinalIgnoreCase);
                    bool isAgendaView = uri.Contains("/r/agenda", StringComparison.OrdinalIgnoreCase);
                    bool isCustomDayView = uri.Contains("/r/customday", StringComparison.OrdinalIgnoreCase);
                    bool isCustomWeekView = uri.Contains("/r/customweek", StringComparison.OrdinalIgnoreCase);
                    bool isSearchView = uri.Contains("/r/search", StringComparison.OrdinalIgnoreCase);
                    // REMOVED: eventedit - event details should open in external window

                    bool isAllowedView = isDayView || isWeekView || isMonthView || isAgendaView || 
                                        isCustomDayView || isCustomWeekView || isSearchView;

                    AppLogger.Info($"    [IsGmailCoreNavigation] View checks:");
                    AppLogger.Info($"      - /r/day → {isDayView}");
                    AppLogger.Info($"      - /r/week → {isWeekView}");
                    AppLogger.Info($"      - /r/month → {isMonthView}");
                    AppLogger.Info($"      - /r/agenda → {isAgendaView}");
                    AppLogger.Info($"      - /r/eventedit → BLOCKED (opens in popup)");
                    AppLogger.Info($"    [IsGmailCoreNavigation] → {isAllowedView} (allowed calendar view)");

                    return isAllowedView;
                }

                // Block everything else (event detail pages with external links)
                AppLogger.Info($"    [IsGmailCoreNavigation] → FALSE (calendar domain but not an allowed view path)");
                return false;
            }

            AppLogger.Info($"    [IsGmailCoreNavigation] → FALSE (not a core Gmail/Calendar domain)");
            return false;
        }

        /// <summary>
        /// Opens a modal <see cref="Dialogs.ExternalBrowserWindow"/> for an external URL.
        /// The window has a clean WebView2 (no Gmail scripts) with download interception.
        /// Modal — the user must close it before returning to the main application.
        /// </summary>
        private static void OpenExternalBrowserWindow(WebView2 webView, string uri, EmailInfo? emailInfo)
        {
            try
            {
                var ownerWindow = Window.GetWindow(webView);
                var browserWindow = new Dialogs.ExternalBrowserWindow(uri, emailInfo);
                if (ownerWindow != null)
                    browserWindow.Owner = ownerWindow;
                browserWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Failed to open external browser - {ex.Message}");
                OpenInSystemBrowser(uri);
            }
        }

        /// <summary>
        /// Opens a URL in the system default browser.
        /// </summary>
        private static void OpenInSystemBrowser(string uri)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Failed to open URL in system browser - {ex.Message}");
            }
        }

        /// <summary>
        /// Intercepts downloads from the main Gmail WebView2 (attachment button clicks).
        /// Silently routes to the ACC-mirrored project path when a project is associated.
        /// External-site downloads are handled by <see cref="Dialogs.ExternalBrowserWindow"/>.
        /// </summary>
        private static void HandleDownloadStarting(WebView2 webView, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                var emailInfo = GetSelectedEmailInfo(webView);
                var rawFileName = Path.GetFileName(e.ResultFilePath);
                var sanitizedFileName = MessageKeyGenerator.SanitizeFileName(rawFileName);
                var ownerWindow = Window.GetWindow(webView);

                var projectName = ResolveProjectNameFromEmail(emailInfo);
                var hasProject = emailInfo != null
                              && !string.IsNullOrEmpty(emailInfo.MessageId)
                              && !string.IsNullOrEmpty(projectName);

                if (emailInfo != null && !string.IsNullOrEmpty(emailInfo.MessageId))
                {
                    var dialog = new DownloadAssociationDialog(
                        sanitizedFileName,
                        hasProject ? projectName : null)
                    {
                        Owner = ownerWindow
                    };

                    var dialogResult = dialog.ShowDialog();
                    if (dialogResult != true || dialog.ChosenAction == DownloadAction.Cancel)
                    {
                        e.Cancel = true;
                        e.Handled = true;
                        return;
                    }

                    switch (dialog.ChosenAction)
                    {
                        case DownloadAction.UploadToAcc:
                        case DownloadAction.AssociateToProject:
                            var accPath = BuildAccMirroredPath(emailInfo, sanitizedFileName);
                            if (!ResolveDuplicateFilePath(ownerWindow, sanitizedFileName, accPath, out var resolvedAccPath))
                            {
                                e.Cancel = true;
                                e.Handled = true;
                                return;
                            }

                            e.ResultFilePath = resolvedAccPath;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"WebView2: Download intercepted → {resolvedAccPath}");
                            TrackDownloadCompletion(e.DownloadOperation, emailInfo);
                            return;

                        case DownloadAction.SaveToDownloads:
                            var downloadsPath = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                "Downloads",
                                sanitizedFileName);

                            if (!ResolveDuplicateFilePath(ownerWindow, sanitizedFileName, downloadsPath, out var resolvedDownloadsPath))
                            {
                                e.Cancel = true;
                                e.Handled = true;
                                return;
                            }

                            e.ResultFilePath = resolvedDownloadsPath;
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine($"WebView2: Download routed to Downloads → {resolvedDownloadsPath}");
                            return;
                    }
                }

                if (hasProject)
                {
                    var accPath = BuildAccMirroredPath(emailInfo!, sanitizedFileName);
                    if (!ResolveDuplicateFilePath(ownerWindow, sanitizedFileName, accPath, out var resolvedAccPath))
                    {
                        e.Cancel = true;
                        e.Handled = true;
                        return;
                    }

                    e.ResultFilePath = resolvedAccPath;
                    e.Handled = true;
                    System.Diagnostics.Debug.WriteLine($"WebView2: Download intercepted → {resolvedAccPath}");
                    TrackDownloadCompletion(e.DownloadOperation, emailInfo);
                }
                else if (emailInfo != null && !string.IsNullOrEmpty(emailInfo.MessageId))
                {
                    // No project assigned — still route to ACC Inbox path so the upload pipeline can handle it
                    var accPath = BuildAccMirroredPath(emailInfo, sanitizedFileName);
                    if (!ResolveDuplicateFilePath(ownerWindow, sanitizedFileName, accPath, out var resolvedAccPath))
                    {
                        e.Cancel = true;
                        e.Handled = true;
                        return;
                    }

                    e.ResultFilePath = resolvedAccPath;
                    e.Handled = true;
                    System.Diagnostics.Debug.WriteLine($"WebView2: Download (no project) → ACC Inbox: {resolvedAccPath}");
                    TrackDownloadCompletion(e.DownloadOperation, emailInfo);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "WebView2: Download started but no email context — using default behavior");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error handling download - {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the ACC-mirrored local path for a downloaded file:
        /// <c>{DownloadBasePath}/_Inbox/{YYYY}/{MM}/MSG_{Key}/Attachments/{FileName}</c>
        /// </summary>
        internal static string BuildAccMirroredPath(EmailInfo emailInfo, string sanitizedFileName)
        {
            var uniqueId = MessageKeyGenerator.GetMessageUniqueId(null, emailInfo.MessageId);
            var messageKey = MessageKeyGenerator.GetMessageKey(uniqueId);

            var date = emailInfo.ParsedDate != DateTime.MinValue ? emailInfo.ParsedDate : DateTime.Now;
            var year = date.ToString("yyyy");
            var month = date.ToString("MM");

            var downloadFolder = Path.Combine(
                AppConfiguration.DownloadBasePath,
                "_Inbox",
                year,
                month,
                $"MSG_{messageKey}",
                "Attachments");

            Directory.CreateDirectory(downloadFolder);
            return Path.Combine(downloadFolder, sanitizedFileName);
        }

        /// <summary>
        /// If the target file already exists, asks the user whether to continue.
        /// If approved, returns a unique path with a numeric suffix.
        /// </summary>
        internal static bool ResolveDuplicateFilePath(
            Window? owner,
            string fileName,
            string proposedPath,
            out string resolvedPath)
        {
            resolvedPath = proposedPath;

            if (!File.Exists(proposedPath))
                return true;

            var result = MessageBox.Show(
                owner ?? Application.Current?.MainWindow,
                $"הקובץ \"{fileName}\" כבר קיים ברשימה.\nהאם להעלות אותו בכל זאת?",
                "קובץ קיים",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                resolvedPath = string.Empty;
                return false;
            }

            var folderPath = Path.GetDirectoryName(proposedPath);
            if (string.IsNullOrWhiteSpace(folderPath))
                return true;

            resolvedPath = BuildUniqueDestinationPath(folderPath, fileName);
            return true;
        }

        /// <summary>
        /// Builds a unique file path in a folder, appending (1), (2), etc. when needed.
        /// </summary>
        private static string BuildUniqueDestinationPath(string folderPath, string fileName)
        {
            var destPath = Path.Combine(folderPath, fileName);

            if (!File.Exists(destPath))
                return destPath;

            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;

            do
            {
                destPath = Path.Combine(folderPath, $"{nameWithoutExt} ({counter}){extension}");
                counter++;
            }
            while (File.Exists(destPath));

            return destPath;
        }

        /// <summary>
        /// Resolves a human-readable project name from the email's label or thread context.
        /// Returns null if no project is associated.
        /// </summary>
        internal static string? ResolveProjectNameFromEmail(EmailInfo? emailInfo)
        {
            if (emailInfo == null) return null;

            if (emailInfo.IsFiledInGmail && !string.IsNullOrEmpty(emailInfo.LabelProjectName))
                return emailInfo.LabelProjectName;

            if (emailInfo.HasThreadHistory && !string.IsNullOrEmpty(emailInfo.ThreadProjectName))
                return emailInfo.ThreadProjectName;

            return null;
        }

        /// <summary>
        /// Tracks download completion and logs the result.
        /// When the download completes and an <see cref="EmailInfo"/> is associated,
        /// raises <see cref="ProjectFileDownloaded"/> so the ViewModel can upload to ACC.
        /// </summary>
        internal static void TrackDownloadCompletion(
            CoreWebView2DownloadOperation downloadOp, EmailInfo? emailInfo)
        {
            downloadOp.StateChanged += (sender, _) =>
            {
                if (sender is not CoreWebView2DownloadOperation op) return;

                switch (op.State)
                {
                    case CoreWebView2DownloadState.Completed:
                        var filePath = op.ResultFilePath;
                        var fileName = System.IO.Path.GetFileName(filePath);
                        System.Diagnostics.Debug.WriteLine(
                            $"WebView2: Download completed → {filePath} ({op.BytesReceived} bytes)");

                        if (emailInfo != null && !string.IsNullOrEmpty(filePath))
                        {
                            ProjectFileDownloaded?.Invoke(filePath, fileName, emailInfo);
                        }
                        break;

                    case CoreWebView2DownloadState.Interrupted:
                        System.Diagnostics.Debug.WriteLine(
                            $"WebView2: Download interrupted → {op.InterruptReason}");
                        break;
                }
            };
        }

        /// <summary>
        /// Configures the virtual host for serving inline images from memory.
        /// This avoids embedding large Base64 strings in HTML which can crash WebView2.
        /// </summary>
        private static void ConfigureVirtualHostForImages(CoreWebView2 coreWebView)
        {
            string virtualHost = InlineImageProvider.VirtualHostName;

            // Add filter for virtual host requests
            coreWebView.AddWebResourceRequestedFilter(
                $"https://{virtualHost}/*",
                CoreWebView2WebResourceContext.Image);

            // Handle requests to the virtual host
            coreWebView.WebResourceRequested += OnWebResourceRequested;

            System.Diagnostics.Debug.WriteLine($"WebView2: Virtual host '{virtualHost}' configured for inline images");
        }

        /// <summary>
        /// Handles WebResourceRequested events for the virtual image host.
        /// Serves inline images from the InlineImageProvider cache.
        /// </summary>
        private static void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Request.Uri);
                
                // Only handle our virtual host
                if (!uri.Host.Equals(InlineImageProvider.VirtualHostName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"WebView2: Virtual host request for: {uri.AbsolutePath}");

                // Get image from cache
                var cachedImage = InlineImageProvider.Instance.GetImage(uri.AbsolutePath);

                if (cachedImage != null && sender is CoreWebView2 coreWebView)
                {
                    // Create response stream from cached data
                    var stream = new MemoryStream(cachedImage.Data);
                    
                    // Create headers
                    string headers = $"Content-Type: {cachedImage.MimeType}\r\n" +
                                    $"Content-Length: {cachedImage.Data.Length}\r\n" +
                                    "Cache-Control: max-age=3600\r\n" +
                                    "Access-Control-Allow-Origin: *";

                    // Create and set the response
                    var response = coreWebView.Environment.CreateWebResourceResponse(
                        stream,
                        200,
                        "OK",
                        headers);

                    e.Response = response;

                    System.Diagnostics.Debug.WriteLine(
                        $"WebView2: Served image {cachedImage.ContentId} ({cachedImage.Data.Length} bytes)");
                }
                else
                {
                    // Return 404 for unknown images
                    if (sender is CoreWebView2 cv)
                    {
                        e.Response = cv.Environment.CreateWebResourceResponse(
                            null,
                            404,
                            "Not Found",
                            "Content-Type: text/plain");
                    }

                    System.Diagnostics.Debug.WriteLine($"WebView2: Image not found for path: {uri.AbsolutePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error handling resource request - {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Phase 1: Persistent UserDataFolder per Google account
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a CoreWebView2Environment with a persistent UserDataFolder
        /// mapped to the current user's email address. This preserves the Google
        /// SSO session across app restarts, eliminating repeated login prompts.
        /// Falls back to default (temp) if no user email is set yet.
        /// </summary>
        internal static async Task<CoreWebView2Environment?> CreateUserEnvironmentAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentUserEmail))
            {
                System.Diagnostics.Debug.WriteLine("WebView2: No CurrentUserEmail set, using default UserDataFolder");
                return null;
            }

            var sanitized = SanitizeEmailForPath(CurrentUserEmail);
            var userDataFolder = Path.Combine(AppConfiguration.WebView2UserDataBasePath, sanitized);

            Directory.CreateDirectory(userDataFolder);
            System.Diagnostics.Debug.WriteLine($"WebView2: Using persistent UserDataFolder: {userDataFolder}");

            return await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
        }

        /// <summary>
        /// Creates a CoreWebView2Environment for the ACC document viewer.
        /// Uses a separate UserDataFolder ('acc_viewer') to isolate the ACC
        /// browser session from the Gmail session.
        /// </summary>
        private static async Task<CoreWebView2Environment> CreateAccEnvironmentAsync()
        {
            var userDataFolder = Path.Combine(AppConfiguration.WebView2UserDataBasePath, "acc_viewer");
            Directory.CreateDirectory(userDataFolder);
            System.Diagnostics.Debug.WriteLine($"WebView2: Using ACC UserDataFolder: {userDataFolder}");
            return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        }

        /// <summary>
        /// Sanitizes an email address for safe use as a file system directory name.
        /// Rules: '@' → '_at_', strip illegal chars, truncate to 100 characters.
        /// </summary>
        internal static string SanitizeEmailForPath(string email)
        {
            var sanitized = email.Trim().ToLowerInvariant();
            sanitized = sanitized.Replace("@", "_at_");
            sanitized = _illegalPathChars.Replace(sanitized, "_");

            if (sanitized.Length > 100)
                sanitized = sanitized[..100];

            return sanitized;
        }

        /// <summary>
        /// Clears the WebView2 browsing session data for the current user.
        /// Call this on logout to wipe cookies, cache, and SSO tokens so that
        /// the next login can use a different Google account.
        /// </summary>
        public static async Task ClearSessionAsync(WebView2 webView)
        {
            try
            {
                if (webView.CoreWebView2?.Profile != null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: Clearing browsing data for session logout");
                    await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                    System.Diagnostics.Debug.WriteLine("WebView2: Browsing data cleared successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: Cannot clear session - CoreWebView2 or Profile is null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error clearing session - {ex.Message}");
            }
        }

        /// <summary>
        /// Injects OAuth session hints into the WebView2 CookieManager after
        /// successful authentication in the system browser. Sets identity cookies
        /// on the Google domain so subsequent Gmail navigations can attempt to
        /// reuse the session without a full re-authentication prompt.
        ///
        /// If the session cannot be established (no existing Google cookies),
        /// <see cref="NavigateToUrlSafely"/> will detect the redirect to
        /// accounts.google.com and fall back to HTML rendering.
        /// </summary>
        public static Task InjectOAuthSessionAsync(WebView2 webView, string email)
        {
            try
            {
                if (webView.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: Cannot inject OAuth session — CoreWebView2 not initialized");
                    CurrentUserEmail = email;
                    return Task.CompletedTask;
                }

                CurrentUserEmail = email;
                var cookieManager = webView.CoreWebView2.CookieManager;

                // Set a login-hint cookie on the Google domain so Gmail navigations
                // can identify the authenticated account without a full sign-in prompt.
                var hintCookie = cookieManager.CreateCookie(
                    "GMAIL_AT", email, ".google.com", "/");
                hintCookie.IsSecure = true;
                hintCookie.IsHttpOnly = false;
                hintCookie.SameSite = CoreWebView2CookieSameSiteKind.None;
                cookieManager.AddOrUpdateCookie(hintCookie);

                System.Diagnostics.Debug.WriteLine($"WebView2: OAuth session hints injected for {email}");

                // Attempt to establish a Gmail session by navigating to the base URL.
                // If Google requires full re-authentication, the NavigationCompleted
                // handler will detect the redirect and fall back to HTML rendering.
                webView.CoreWebView2.Navigate(GmailBaseUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error injecting OAuth session - {ex.Message}");
            }

            return Task.CompletedTask;
        }

        // ══════════════════════════════════════════════════════════════════
        // Phase 2: URL navigation with fallback to HTML rendering
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Navigates WebView2 to a URL (Gmail popout). If navigation fails
        /// (network error, session expired), falls back to FallbackHtml rendering.
        /// </summary>
        /// <summary>
        /// Base Gmail URL used as the initial landing page when the user needs to
        /// authenticate inside the WebView2 control (no existing session).
        /// </summary>
        private const string GmailBaseUrl = "https://mail.google.com/mail/u/0/";

        /// <summary>
        /// Grace period (ms) before falling back to HTML after a non-terminal navigation
        /// error (e.g. <see cref="CoreWebView2WebErrorStatus.Unknown"/>). Allows time for
        /// Google redirect chains and late-loading resources to complete.
        /// </summary>
        private const int FallbackGracePeriodMs = 3000;

        private static void NavigateToUrlSafely(WebView2 webView, WebView2State state, string? url)
        {
            try
            {
                if (webView.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: CoreWebView2 is null, cannot navigate to URL");
                    return;
                }

                if (string.IsNullOrEmpty(url))
                {
                    NavigateToHtmlSafely(webView, GetEmptyContentHtml());
                    return;
                }

                var targetUrl = url;
                if (!GetIsAccViewer(webView) && string.IsNullOrWhiteSpace(CurrentUserEmail))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "WebView2: No active session \u2014 landing on Gmail base login instead of deep-link");
                    targetUrl = GmailBaseUrl;
                }

                System.Diagnostics.Debug.WriteLine($"WebView2: Navigating to URL: {targetUrl}");

                // Invalidate any previous navigation's handler and cancel pending fallback timer
                var generation = ++state.NavigationGeneration;
                state.FallbackDelayCts?.Cancel();
                state.FallbackDelayCts = null;

                // NavigationCompleted handler \u2014 stays subscribed across non-terminal failures
                // and detaches on success, terminal error, or stale generation.
                void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    // Stale handler from a superseded navigation \u2014 detach and exit
                    if (state.NavigationGeneration != generation)
                    {
                        webView.CoreWebView2!.NavigationCompleted -= OnNavigationCompleted;
                        return;
                    }

                    // Safe: this handler was raised by CoreWebView2 so it cannot be null here.
                    var coreWv = webView.CoreWebView2!;
                    var currentUri = coreWv.Source ?? "(unavailable)";
                    System.Diagnostics.Debug.WriteLine(
                        $"WebView2: NavigationCompleted \u2014 IsSuccess={e.IsSuccess}, " +
                        $"Status={e.WebErrorStatus}, Uri={currentUri}");

                    if (!e.IsSuccess)
                    {
                        // Google domain bypass: let the page load even if WebView2
                        // reports errors \u2014 Google's 2026 security headers can trigger
                        // spurious 404/8464 codes that resolve once the SPA boots.
                        if (IsGoogleDomain(currentUri))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"WebView2: Error {e.WebErrorStatus} on Google domain \u2014 ignoring, letting page load.");
                            return;
                        }

                        // Tier 1 \u2014 Redirect chain: stay subscribed for the next event
                        if (e.WebErrorStatus is CoreWebView2WebErrorStatus.ConnectionAborted
                                             or CoreWebView2WebErrorStatus.OperationCanceled)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"WebView2: Redirect ({e.WebErrorStatus}), waiting for next navigation.");
                            return;
                        }

                        // Tier 2 \u2014 Terminal network/cert errors: immediate fallback
                        if (IsTerminalNavigationError(e.WebErrorStatus))
                        {
                            coreWv.NavigationCompleted -= OnNavigationCompleted;
                            System.Diagnostics.Debug.WriteLine(
                                $"WebView2: Terminal error ({e.WebErrorStatus}). Falling back to HTML.");
                            FallbackToHtml(webView, state);
                            return;
                        }

                        // Tier 3 \u2014 Non-terminal (Unknown, etc.): the page may still be loading.
                        // Stay subscribed and start a grace-period timer.
                        System.Diagnostics.Debug.WriteLine(
                            $"WebView2: Non-terminal status ({e.WebErrorStatus}). " +
                            $"Starting {FallbackGracePeriodMs}ms grace period.");
                        StartFallbackGracePeriod(webView, state, generation, () =>
                        {
                            coreWv.NavigationCompleted -= OnNavigationCompleted;
                        });
                        return;
                    }

                    // \u2500\u2500 Success \u2500\u2500
                    coreWv.NavigationCompleted -= OnNavigationCompleted;
                    state.FallbackDelayCts?.Cancel();

                    // Session-expired redirect to Google sign-in — let the user
                    // re-authenticate directly in the WebView2 instead of hiding the page.
                    if (!string.IsNullOrEmpty(currentUri) &&
                        currentUri.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "WebView2: Redirect to Google sign-in (session expired). Letting user re-authenticate in-place.");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"WebView2: Navigation succeeded: {currentUri}");
                }

                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                webView.CoreWebView2.Navigate(targetUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error navigating to URL - {ex.Message}");
                FallbackToHtml(webView, state);
            }
        }

        /// <summary>
        /// Returns <c>true</c> for <see cref="CoreWebView2WebErrorStatus"/> values that indicate
        /// an unrecoverable failure (DNS, connection, certificate errors). These warrant an
        /// immediate fallback with no grace period.
        /// </summary>
        private static bool IsTerminalNavigationError(CoreWebView2WebErrorStatus status) =>
            status is CoreWebView2WebErrorStatus.ServerUnreachable
                   or CoreWebView2WebErrorStatus.Timeout
                   or CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse
                   or CoreWebView2WebErrorStatus.HostNameNotResolved
                   or CoreWebView2WebErrorStatus.Disconnected
                   or CoreWebView2WebErrorStatus.ConnectionReset
                   or CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect
                   or CoreWebView2WebErrorStatus.CertificateExpired
                   or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors
                   or CoreWebView2WebErrorStatus.CertificateRevoked
                   or CoreWebView2WebErrorStatus.CertificateIsInvalid;

        /// <summary>
        /// Starts a delayed fallback timer. If the grace period elapses without a
        /// successful navigation (or a newer navigation superseding this one), the
        /// fallback HTML is rendered and the NavigationCompleted handler is detached.
        /// </summary>
        private static async void StartFallbackGracePeriod(
            WebView2 webView,
            WebView2State state,
            long generation,
            Action unsubscribeHandler)
        {
            state.FallbackDelayCts?.Cancel();

            var cts = new CancellationTokenSource();
            state.FallbackDelayCts = cts;

            try
            {
                await Task.Delay(FallbackGracePeriodMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "WebView2: Grace period cancelled (navigation succeeded or superseded).");
                return;
            }

            // Stale generation \u2014 a newer navigation took over
            if (state.NavigationGeneration != generation)
            {
                System.Diagnostics.Debug.WriteLine(
                    "WebView2: Grace period fired for stale generation, ignoring.");
                return;
            }

            // Check if Gmail actually loaded during the wait
            var currentUri = webView.CoreWebView2?.Source;
            if (!string.IsNullOrEmpty(currentUri) &&
                currentUri.Contains("mail.google.com", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WebView2: Grace period elapsed but Gmail loaded ({currentUri}). Skipping fallback.");
                unsubscribeHandler();
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"WebView2: Grace period elapsed, page not on Gmail ({currentUri}). Falling back to HTML.");
            unsubscribeHandler();
            FallbackToHtml(webView, state);
        }

        /// <summary>
        /// Checks whether a URI is on a Google domain (mail, accounts, APIs, static assets).
        /// Used by the NavigationCompleted handler to suppress fallback on Google pages.
        /// </summary>
        private static bool IsGoogleDomain(string? uri) =>
            !string.IsNullOrEmpty(uri) &&
            (uri.Contains("google.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Injects the persistent Gmail clean-view script via <c>ExecuteScriptAsync</c>.
        /// Called from the <see cref="CoreWebView2.DOMContentLoaded"/> handler so the
        /// MutationObserver is active before Gmail finishes rendering.
        /// </summary>
        private static async Task InjectCleanViewAsync(CoreWebView2 coreWebView)
        {
            try
            {
                await coreWebView.ExecuteScriptAsync(GmailCleanViewJs);
                System.Diagnostics.Debug.WriteLine("WebView2: Gmail clean-view MutationObserver injected");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Error injecting clean-view script - {ex.Message}");
            }
        }

        /// <summary>
        /// Injects the calendar clean-view script (<see cref="CalendarCleanViewJs"/>)
        /// that isolates the <c>.brB-Jz</c> container and hides all surrounding UI.
        /// Called from the <see cref="CoreWebView2.DOMContentLoaded"/> handler for
        /// <c>calendar.google.com</c> navigations.
        /// </summary>
        private static async Task InjectCalendarCleanViewAsync(CoreWebView2 coreWebView)
        {
            try
            {
                AppLogger.Info("[Calendar JS Injection] Injecting calendar clean-view script with link interceptor...");
                await coreWebView.ExecuteScriptAsync(CalendarCleanViewJs);
                AppLogger.Info("[Calendar JS Injection] ✓ Calendar script injected successfully");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[Calendar JS Injection] Failed to inject calendar script");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Calendar Sidebar: Dedicated initialization (decoupled from email)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a WebView2 for Google Calendar and navigates once.
        /// Shares the same <see cref="CoreWebView2Environment"/> (persistent UserDataFolder)
        /// as the email WebView2, so authenticated session cookies are reused.
        ///
        /// Call AFTER authentication (<see cref="CurrentUserEmail"/> is set) so the
        /// calendar shares the same Google session. Subsequent calls are no-ops.
        ///
        /// Uses the day view (<c>/r/day</c>) with content isolation via
        /// <see cref="CalendarCleanViewJs"/> injected on DOMContentLoaded.
        /// </summary>
        public static async Task InitializeCalendarAsync(WebView2 webView, CancellationToken ct = default)
        {
            System.Diagnostics.Debug.WriteLine($"╔═══════════════════════════════════════════════╗");
            System.Diagnostics.Debug.WriteLine($"║ [CALENDAR INIT] InitializeCalendarAsync START ║");
            System.Diagnostics.Debug.WriteLine($"╚═══════════════════════════════════════════════╝");

            var state = _webViewStates.GetOrAdd(webView, _ => new WebView2State());

            // Already initialized and navigated
            if (state.IsInitialized && webView.CoreWebView2 != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Already initialized, skipping");
                return;
            }

            if (!await state.InitSemaphore.WaitAsync(0))
            {
                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Init already in progress, skipping");
                return;
            }

            try
            {
                if (state.IsInitialized && webView.CoreWebView2 != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Double-check: Already initialized, skipping");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Proceeding with initialization...");

                if (!webView.Dispatcher.CheckAccess())
                {
                    System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Not on UI thread, dispatching...");
                    await await webView.Dispatcher.InvokeAsync(
                        () => InitializeCalendarCoreAsync(webView, state));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] On UI thread, initializing directly...");
                    await InitializeCalendarCoreAsync(webView, state);
                }

                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] ✓ Initialization completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] ✗ Initialization failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CALENDAR INIT] Stack trace: {ex.StackTrace}");
                state.IsInitialized = false;
            }
            finally
            {
                state.InitSemaphore.Release();
            }
        }

        /// <summary>
        /// Core initialization for the Calendar WebView2. Must run on the UI thread.
        /// Creates the environment (shared profile), configures browser behavior
        /// (UA masking, navigation guards), and navigates to the Google Calendar
        /// day view (<see cref="CalendarDayViewUrl"/>). Content isolation is handled
        /// by <see cref="CalendarCleanViewJs"/> injected via DOMContentLoaded.
        /// </summary>
        private static async Task InitializeCalendarCoreAsync(WebView2 webView, WebView2State state)
        {
            AppLogger.Info($"[CALENDAR INIT CORE] Starting core initialization...");
            try
            {
                if (webView.CoreWebView2 == null)
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] CoreWebView2 is null, creating environment...");
                    var environment = await CreateUserEnvironmentAsync();
                    AppLogger.Info($"[CALENDAR INIT CORE] Environment created, ensuring CoreWebView2...");
                    await webView.EnsureCoreWebView2Async(environment);
                    AppLogger.Info($"[CALENDAR INIT CORE] ✓ CoreWebView2 initialized");
                }
                else
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] CoreWebView2 already exists");
                }

                if (!state.BrowserConfigured && webView.CoreWebView2 != null)
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] Configuring browser behavior (Navigation guards, UA, etc.)...");
                    ConfigureBrowserBehavior(webView);
                    state.BrowserConfigured = true;
                    AppLogger.Info($"[CALENDAR INIT CORE] ✓ Browser behavior configured");
                }
                else
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] Browser already configured or CoreWebView2 is null");
                }

                state.IsInitialized = true;
                AppLogger.Info($"[CALENDAR INIT CORE] State marked as initialized");

                if (webView.CoreWebView2 != null)
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] Navigating to: {CalendarDayViewUrl}");
                    webView.CoreWebView2.Navigate(CalendarDayViewUrl);
                    AppLogger.Info($"[CALENDAR INIT CORE] ✓ Navigation command sent");
                }
                else
                {
                    AppLogger.Info($"[CALENDAR INIT CORE] ✗ Cannot navigate - CoreWebView2 is null");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"[CALENDAR INIT CORE] ERROR");
                state.IsInitialized = false;
            }
        }

        /// <summary>
        /// Falls back to rendering the FallbackHtml (HtmlBodyForDisplay) when URL navigation fails.
        /// </summary>
        private static void FallbackToHtml(WebView2 webView, WebView2State state)
        {
            string? fallbackHtml;
            lock (state.Lock)
            {
                fallbackHtml = state.FallbackHtml;
            }

            // Re-read from the attached property if state didn't capture it.
            // This covers the case where FallbackHtml was data-bound AFTER
            // the NavigateUrl change that triggered initialization.
            if (string.IsNullOrEmpty(fallbackHtml))
            {
                try
                {
                    fallbackHtml = webView.Dispatcher.CheckAccess()
                        ? GetFallbackHtml(webView)
                        : webView.Dispatcher.Invoke(() => GetFallbackHtml(webView));

                    if (!string.IsNullOrEmpty(fallbackHtml))
                    {
                        lock (state.Lock)
                        {
                            state.FallbackHtml = fallbackHtml;
                        }
                        System.Diagnostics.Debug.WriteLine("WebView2: FallbackHtml recovered from attached property");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WebView2: Error reading FallbackHtml property - {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(fallbackHtml))
            {
                System.Diagnostics.Debug.WriteLine("WebView2: Rendering fallback HTML content");
                NavigateToHtmlSafely(webView, fallbackHtml);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("WebView2: No fallback HTML available, showing error");
                NavigateToHtmlSafely(webView, GetErrorHtml("Could not load email. Please try again."));
            }
        }

        private static void NavigateToHtmlSafely(WebView2 webView, string? htmlContent)
        {
            try
            {
                // Verify CoreWebView2 is ready
                if (webView.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: CoreWebView2 is null, cannot navigate");
                    return;
                }

                // Navigate to content or empty placeholder
                if (!string.IsNullOrEmpty(htmlContent))
                {
                    // Log size for debugging large content issues
                    int lengthKb = htmlContent.Length / 1024;
                    System.Diagnostics.Debug.WriteLine(
                        $"WebView2: Navigating to HTML content ({lengthKb} KB, {htmlContent.Length} chars)");
                    
                    webView.NavigateToString(htmlContent);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WebView2: Navigating to empty placeholder");
                    webView.NavigateToString(GetEmptyContentHtml());
                }
            }
            catch (ArgumentException ex)
            {
                // Handle "Value does not fall within the expected range" error
                System.Diagnostics.Debug.WriteLine($"WebView2: ArgumentException during navigation - {ex.Message}");
                
                try
                {
                    webView.NavigateToString(GetErrorHtml("Content too large to display. Try a different email."));
                }
                catch
                {
                    // Silently fail
                }
            }
            catch (InvalidOperationException ex)
            {
                // WebView2 might be disposed or in invalid state
                System.Diagnostics.Debug.WriteLine($"WebView2: InvalidOperationException - {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2: Unexpected error during navigation - {ex.Message}");
            }
        }

        private static string GetEmptyContentHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body { 
            font-family: 'Segoe UI', Tahoma, sans-serif; 
            display: flex; 
            justify-content: center; 
            align-items: center; 
            height: 100vh; 
            margin: 0;
            background-color: #f5f5f5;
            color: #999;
        }
    </style>
</head>
<body>
    <p>Select an email to view its content</p>
</body>
</html>";
        }

        private static string GetErrorHtml(string message)
        {
            var escapedMessage = System.Net.WebUtility.HtmlEncode(message);
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ 
            font-family: 'Segoe UI', Tahoma, sans-serif; 
            display: flex; 
            justify-content: center; 
            align-items: center; 
            height: 100vh; 
            margin: 0;
            background-color: #fff3cd;
            color: #856404;
        }}
        .error-container {{
            text-align: center;
            padding: 20px;
        }}
        .icon {{ font-size: 48px; margin-bottom: 10px; }}
    </style>
</head>
<body>
    <div class=""error-container"">
        <div class=""icon"">??</div>
        <p>{escapedMessage}</p>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// Cleanup method to remove tracked state when WebView2 is unloaded.
        /// Call this from the Unloaded event of the containing control.
        /// </summary>
        public static void CleanupWebView(WebView2 webView)
        {
            // Remove WebResourceRequested handler to prevent memory leaks
            if (webView.CoreWebView2 != null)
            {
                try
                {
                    webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                }
                catch
                {
                    // Ignore if already disposed
                }
            }

            _webViewStates.TryRemove(webView, out var removedState);
            removedState?.Dispose();
            
            // Clear cached images
            InlineImageProvider.Instance.ClearAll();

            System.Diagnostics.Debug.WriteLine("WebView2: Cleanup completed");
        }
    }
}
