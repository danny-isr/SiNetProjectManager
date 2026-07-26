namespace SiNet.Application.ProjectWork;

/// <summary>
/// Host-provided embedded ACC document viewer (WebView2 in the production V2 host). Mirrors the
/// <c>IEmailBodyRenderer</c> seam: the WPF/WebView2 implementation lives in the host, while the clean
/// ProjectWork surface depends only on this abstraction. When unavailable, the surface falls back to
/// opening ACC documents in an external browser.
/// <para>
/// The viewer manages a strip of tabs (one persistent web view per tab, keyed by
/// <see cref="AccViewerTabRequest.TabKey"/>) so navigation state is preserved while switching tabs.
/// </para>
/// </summary>
public interface IAccViewerHost
{
    /// <summary>True when an embedded viewer implementation is available.</summary>
    bool IsAvailable { get; }

    /// <summary>Maximum number of concurrently open tabs the viewer allows.</summary>
    int MaxTabs { get; }

    /// <summary>Raised after a tab is closed (X on the strip or explicit <see cref="CloseTab"/>).</summary>
    event Action<string>? TabClosed;

    /// <summary>Attaches the viewer to a WPF host element (e.g. a <c>ContentControl</c> or panel).</summary>
    void AttachHost(object hostElement);

    /// <summary>
    /// Opens a new tab for the request, or activates the existing tab with the same
    /// <see cref="AccViewerTabRequest.TabKey"/>. Returns <see langword="false"/> when the tab limit is
    /// reached or the host is not ready.
    /// </summary>
    Task<bool> OpenOrActivateTabAsync(AccViewerTabRequest request, CancellationToken cancellationToken = default);

    /// <summary>True when a tab with <paramref name="tabKey"/> is currently open.</summary>
    bool IsTabOpen(string tabKey);

    /// <summary>Closes the tab identified by <paramref name="tabKey"/>, if open.</summary>
    void CloseTab(string tabKey);

    /// <summary>Closes all tabs and clears the viewer.</summary>
    void Clear();
}

/// <summary>Request to open/activate an ACC viewer tab.</summary>
/// <param name="TabKey">Stable identity of the tab (e.g. ACC item id) used for de-duplication.</param>
/// <param name="Title">Tab title shown in the strip.</param>
/// <param name="ViewerUrl">Resolved ACC viewer URL to navigate to.</param>
public sealed record AccViewerTabRequest(
    string TabKey,
    string Title,
    string ViewerUrl);
