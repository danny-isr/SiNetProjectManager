// ─────────────────────────────────────────────────────────────────────────────
// DISABLED LEGACY — candidate for physical deletion in a future approved round.
//
// Reason:        Gmail DOM is not a source of truth for attachments.
//                The Gmail DOM shows a thread/conversation view, not a clean
//                message-scoped view, so DOM-derived attachment lists are
//                unreliable.
// Disabled by:   Gap 8 (DocumentationVsImplementationGaps-2026-05-26.md)
// Status:        Commented out / staged for future physical deletion.
// Required before physical deletion:
//                1. Confirmation that no future round re-introduces a DOM
//                   probe at this location.
//                2. Explicit approval to delete the file, the DI registration,
//                   and the EmailManagementView field/no-op method.
//                3. Verification that build + full test suite still pass with
//                   the file physically removed.
//
// Do not re-enable without explicit approval. The entire body is parked behind
// `#if false` so the symbols are not visible to the compiler but the source
// history remains intact for the future cleanup round.
// ─────────────────────────────────────────────────────────────────────────────
#if false
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SiNetSQL.Services;
using SiOffice.GoogleConnector;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// <b>DISABLED — diagnostic disabled — not used currently — candidate for future removal.</b>
/// <para>
/// This extractor probed the live Gmail WebView2 DOM for the "attachment chips/tiles"
/// area. It was disabled because the Gmail DOM exposes a thread/conversation view
/// rather than a clean message-scoped view, so DOM-derived attachment lists cannot
/// be trusted for the current single-message semantics.
/// </para>
/// <para><b>Current behavior:</b> <see cref="ProbeAsync"/> is a no-op that emits a
/// single <c>[GmailVisibleAttachmentsDom] Disabled</c> log line and returns
/// <see cref="GmailVisibleAttachmentsDomResult.Skipped(string)"/>. The class is kept
/// only to avoid breaking the DI graph and existing call sites; do not re-enable
/// without an explicit approved round.</para>
/// <para><b>Must NOT be used for:</b> attachment detection, validation, upload,
/// AlreadyProcessed, PDF, or reconciliation.</para>
/// </summary>
public sealed class GmailVisibleAttachmentsDomExtractor
{
    private readonly WebView2PdfRenderer _pdfRenderer;

    /// <summary>Bound to the WebView2 registered through <see cref="WebView2PdfRenderer"/>.</summary>
    public GmailVisibleAttachmentsDomExtractor(WebView2PdfRenderer pdfRenderer)
    {
        _pdfRenderer = pdfRenderer;
    }

    /// <summary>
    /// DISABLED no-op. Always returns <see cref="GmailVisibleAttachmentsDomResult.Skipped(string)"/>
    /// with reason <c>"DisabledForRound"</c> and emits a single
    /// <c>[GmailVisibleAttachmentsDom] Disabled</c> log line. Never throws.
    /// <para>Do not re-enable without an explicit approved round; see class-level remarks.</para>
    /// </summary>
    public Task<GmailVisibleAttachmentsDomResult> ProbeAsync(
        EmailInfo? email, CancellationToken ct = default)
    {
        AppLogger.Info(
            "[GmailVisibleAttachmentsDom] Disabled. " +
            $"MessageId={email?.MessageId}, ThreadId={email?.ThreadId}, " +
            "Reason=DisabledForRound (Gmail DOM shows thread/conversation, not message-scoped view)");
        return Task.FromResult(GmailVisibleAttachmentsDomResult.Skipped("DisabledForRound"));
    }

    /// <summary>
    /// Legacy probe implementation kept for reference only — never invoked while the
    /// public <see cref="ProbeAsync"/> short-circuits to disabled. Marked private so
    /// no external caller can reach it. Candidate for full removal in a future round.
    /// </summary>
    private async Task<GmailVisibleAttachmentsDomResult> ProbeAsync_DisabledLegacy(
        EmailInfo? email, CancellationToken ct = default)
    {
        if (email == null)
        {
            return GmailVisibleAttachmentsDomResult.Skipped("EmailInfoNull");
        }

        var webView = GetLiveWebView();
        if (webView == null)
        {
            AppLogger.Info(
                "[GmailVisibleAttachmentsDom] Skipped. " +
                $"MessageId={email.MessageId}, ThreadId={email.ThreadId}, " +
                "Reason=LiveViewNotRegistered");
            return GmailVisibleAttachmentsDomResult.Skipped("LiveViewNotRegistered");
        }

        var dispatcher = Application.Current?.Dispatcher;
        GmailVisibleAttachmentsDomResult result;
        try
        {
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                result = await dispatcher.InvokeAsync(
                    async () => await ProbeCoreAsync(webView, email, ct),
                    DispatcherPriority.Background, ct).Task.Unwrap();
            }
            else
            {
                result = await ProbeCoreAsync(webView, email, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return GmailVisibleAttachmentsDomResult.Skipped("Cancelled");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "[GmailVisibleAttachmentsDom] ExtractionFailed. " +
                $"MessageId={email.MessageId}, ThreadId={email.ThreadId}, " +
                $"Error={ex.GetType().Name}: {ex.Message}");
            return GmailVisibleAttachmentsDomResult.Failed(ex.Message);
        }

        EmitCompareLog(email, result);
        return result;
    }

    private WebView2? GetLiveWebView()
    {
        // We reuse the same live WebView2 already registered with WebView2PdfRenderer.
        // No reflection-only public field exists, so we rely on the renderer state
        // indirectly via IsLiveViewAvailable + a one-shot script test below.
        if (!_pdfRenderer.IsLiveViewAvailable) return null;

        // The renderer exposes its live view only internally. To avoid adding a
        // new accessor for diagnostic-only work, we use reflection on the
        // private _liveWebView field. This stays compatible with the existing
        // architecture (single registration path).
        var field = typeof(WebView2PdfRenderer).GetField(
            "_liveWebView",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(_pdfRenderer) as WebView2;
    }

    private async Task<GmailVisibleAttachmentsDomResult> ProbeCoreAsync(
        WebView2 webView, EmailInfo email, CancellationToken ct)
    {
        var coreWv = webView.CoreWebView2;
        if (coreWv == null)
        {
            AppLogger.Info(
                "[GmailVisibleAttachmentsDom] Skipped. " +
                $"MessageId={email.MessageId}, Reason=CoreWebView2Null");
            return GmailVisibleAttachmentsDomResult.Skipped("CoreWebView2Null");
        }

        // Quick readiness check (short, bounded — no blind delays).
        var bodyReady = await WaitForConditionAsync(
            coreWv,
            "document.readyState === 'complete' && !!(document.querySelector('.ii.gt') || document.querySelector('.a3s'))",
            timeoutMs: 3000, pollMs: 150, ct);

        if (!bodyReady)
        {
            AppLogger.Info(
                "[GmailVisibleAttachmentsDom] BodyNotReady. " +
                $"MessageId={email.MessageId}, ThreadId={email.ThreadId}");
        }

        ct.ThrowIfCancellationRequested();

        var raw = await coreWv.ExecuteScriptAsync(GmailAttachmentChipsScript);
        var result = ParseScriptResult(raw);

        AppLogger.Info(
            "[GmailVisibleAttachmentsDom] " +
            $"MessageId={email.MessageId}, ThreadId={email.ThreadId}, " +
            $"ExtractionSucceeded={result.ExtractionSucceeded}, " +
            $"VisibleAttachmentCount={result.VisibleAttachments.Count}, " +
            $"VisibleAttachmentNames=[{string.Join("|", result.VisibleAttachments.Select(a => a.FileName ?? ""))}], " +
            $"VisibleAttachmentSizes=[{string.Join("|", result.VisibleAttachments.Select(a => a.DisplaySize ?? ""))}], " +
            $"DomSelectorUsed={result.DomSelectorUsed ?? "(none)"}, " +
            $"Error={result.Error ?? "(none)"}");

        return result;
    }

    private static void EmitCompareLog(EmailInfo email, GmailVisibleAttachmentsDomResult result)
    {
        if (email.Attachments == null || email.Attachments.Count == 0) return;

        foreach (var mime in email.Attachments)
        {
            var match = FindBestMatch(mime, result.VisibleAttachments);
            var visible = match != null;
            string finalCandidate = mime.IsInline
                ? "Inline_NotUploadable"
                : (visible ? "Real_VisibleInGmail" : "Real_ButNotVisibleInGmail");

            AppLogger.Info(
                "[AttachmentVisibilityCompare] " +
                $"MessageId={email.MessageId}, " +
                $"FileName={Quote(mime.FileName)}, " +
                $"MimeType={Quote(mime.MimeType)}, " +
                $"Size={mime.Size}, " +
                $"AttachmentId={Quote(mime.AttachmentId)}, " +
                $"ContentDisposition={Quote(null)}, " +
                $"ContentId={Quote(mime.ContentId)}, " +
                $"MimeFinalIsInline={mime.IsInline}, " +
                $"MimeIsUploadable={!mime.IsInline}, " +
                $"VisibleInGmailDom={visible}, " +
                $"DomMatchedName={Quote(match?.FileName)}, " +
                $"FinalDecisionCandidate={finalCandidate}");
        }
    }

    private static GmailVisibleDomAttachment? FindBestMatch(
        EmailAttachment mime,
        System.Collections.Generic.IReadOnlyList<GmailVisibleDomAttachment> visible)
    {
        if (visible.Count == 0) return null;
        var mimeName = mime.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mimeName)) return null;

        // Exact case-insensitive match wins.
        var exact = visible.FirstOrDefault(v =>
            string.Equals(v.FileName, mimeName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Fallback: match by base filename without path separators.
        var mimeBase = Path.GetFileName(mimeName);
        return visible.FirstOrDefault(v =>
            !string.IsNullOrEmpty(v.FileName) &&
            string.Equals(Path.GetFileName(v.FileName), mimeBase, StringComparison.OrdinalIgnoreCase));
    }

    private static string Quote(string? s) =>
        s == null ? "(null)" : "\"" + s.Replace("\"", "\\\"") + "\"";

    private static async Task<bool> WaitForConditionAsync(
        CoreWebView2 coreWv, string jsCondition, int timeoutMs, int pollMs, CancellationToken ct)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs && !ct.IsCancellationRequested)
        {
            try
            {
                var r = await coreWv.ExecuteScriptAsync(jsCondition);
                if (r != null && r.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { /* ignore transient */ }
            await Task.Delay(pollMs, ct);
        }
        return false;
    }

    private static GmailVisibleAttachmentsDomResult ParseScriptResult(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "null")
            return GmailVisibleAttachmentsDomResult.Failed("EmptyScriptResult");

        try
        {
            // ExecuteScriptAsync returns a JSON-encoded JSON string.
            using var outer = JsonDocument.Parse(raw);
            string inner = outer.RootElement.ValueKind == JsonValueKind.String
                ? outer.RootElement.GetString() ?? "{}"
                : raw;

            using var doc = JsonDocument.Parse(inner);
            var root = doc.RootElement;

            var list = new System.Collections.Generic.List<GmailVisibleDomAttachment>();
            string? selectorUsed = null;
            string? error = null;
            bool ok = false;

            if (root.TryGetProperty("ok", out var okEl)) ok = okEl.GetBoolean();
            if (root.TryGetProperty("selector", out var selEl) && selEl.ValueKind == JsonValueKind.String)
                selectorUsed = selEl.GetString();
            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                error = errEl.GetString();

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    list.Add(new GmailVisibleDomAttachment
                    {
                        FileName    = TryStr(item, "fileName"),
                        DisplaySize = TryStr(item, "displaySize"),
                        AriaLabel   = TryStr(item, "ariaLabel"),
                        Title       = TryStr(item, "title"),
                        InnerText   = TryStr(item, "innerText"),
                        DownloadUrl = TryStr(item, "downloadUrl"),
                        SelectorPath = TryStr(item, "selectorPath"),
                    });
                }
            }

            return new GmailVisibleAttachmentsDomResult
            {
                ExtractionSucceeded = ok,
                VisibleAttachments = list,
                DomSelectorUsed = selectorUsed,
                Error = error,
            };
        }
        catch (Exception ex)
        {
            return GmailVisibleAttachmentsDomResult.Failed($"ParseError:{ex.Message}");
        }
    }

    private static string? TryStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// JavaScript that walks the Gmail DOM looking for the attachment chip area
    /// at the bottom of an open email. We prefer stable, semantic selectors
    /// (aria-label, download anchors, role attributes) over class names, but
    /// fall back to Gmail-specific class hooks when needed.
    /// Returns a JSON string with shape:
    /// { ok: bool, selector: string, error: string|null,
    ///   items: [{ fileName, displaySize, ariaLabel, title, innerText, downloadUrl, selectorPath }] }
    /// </summary>
    private const string GmailAttachmentChipsScript = @"
(function() {
    function safe(fn) { try { return fn(); } catch(e) { return null; } }
    function txt(el) {
        if (!el) return '';
        return ((el.innerText || el.textContent || '') + '').trim();
    }
    function attr(el, n) {
        if (!el) return null;
        var v = el.getAttribute && el.getAttribute(n);
        return v ? v.trim() : null;
    }
    function pathOf(el) {
        if (!el) return null;
        var parts = [];
        var cur = el;
        var depth = 0;
        while (cur && cur.nodeType === 1 && depth < 6) {
            var tag = cur.tagName ? cur.tagName.toLowerCase() : '';
            var cls = (cur.className && typeof cur.className === 'string')
                ? '.' + cur.className.trim().split(/\s+/).slice(0,2).join('.')
                : '';
            parts.unshift(tag + cls);
            cur = cur.parentElement;
            depth++;
        }
        return parts.join(' > ');
    }
    function pushItem(out, root, sourceSelector) {
        // Filename heuristic: prefer download attribute, aria-label, title, then text.
        var download = attr(root, 'download');
        var aria     = attr(root, 'aria-label');
        var title    = attr(root, 'title');
        var inner    = txt(root);

        // Gmail typical chip: aria-label = 'Download attachment Foo.pdf' or 'Foo.pdf'.
        var name = download || null;
        if (!name && aria) {
            var m = aria.match(/(?:Download(?:\s+attachment)?:?\s*)?(.+?\.[A-Za-z0-9]{1,8})\b/);
            name = m ? m[1] : aria;
        }
        if (!name && title) name = title;
        if (!name && inner) {
            var m2 = inner.match(/([^\s\n\r\t]+?\.[A-Za-z0-9]{1,8})\b/);
            name = m2 ? m2[1] : null;
        }

        // Size: Gmail renders e.g. '1.2 MB' or '142 KB' next to chip.
        var size = null;
        if (inner) {
            var sm = inner.match(/(\d+(?:[.,]\d+)?\s*(?:KB|MB|GB|B))/i);
            if (sm) size = sm[1];
        }

        // Download URL: try anchor href or data-* attributes.
        var url = null;
        var anchor = root.tagName === 'A' ? root : root.querySelector && root.querySelector('a[href]');
        if (anchor) url = attr(anchor, 'href');

        out.push({
            fileName: name || '',
            displaySize: size || '',
            ariaLabel: aria || '',
            title: title || '',
            innerText: inner ? inner.substring(0, 240) : '',
            downloadUrl: url || '',
            selectorPath: pathOf(root) + ' (' + sourceSelector + ')'
        });
    }

    try {
        // Locate the open email message container first to scope the search.
        var bodyEl = document.querySelector('.ii.gt') || document.querySelector('.a3s');
        var scope  = bodyEl ? (bodyEl.closest('.adn.ads') || bodyEl.closest('.nH.bkK') || document) : document;

        // Try selectors in priority order. Gmail uses a handful of stable hooks.
        var selectors = [
            // Modern Gmail attachment chip container/anchor.
            { sel: 'div.aQH span.aZo', label: 'div.aQH span.aZo' },
            { sel: 'span.aZo',          label: 'span.aZo' },
            { sel: 'div.aQH',           label: 'div.aQH' },
            // aria-labelled download anchors.
            { sel: 'a[download][href]', label: 'a[download]' },
            { sel: 'a[aria-label*=""Download""]', label: 'a[aria-label*=Download]' },
            // Role-based fallback.
            { sel: '[role=""listitem""][aria-label]', label: 'role=listitem' }
        ];

        for (var i = 0; i < selectors.length; i++) {
            var nodes = scope.querySelectorAll
                ? scope.querySelectorAll(selectors[i].sel)
                : [];
            if (nodes && nodes.length > 0) {
                var out = [];
                for (var j = 0; j < nodes.length; j++) {
                    pushItem(out, nodes[j], selectors[i].label);
                }
                // Filter out items where we could not derive a filename.
                var named = out.filter(function(x) { return x.fileName && x.fileName.length > 0; });
                if (named.length > 0) {
                    return JSON.stringify({
                        ok: true,
                        selector: selectors[i].label,
                        error: null,
                        items: named
                    });
                }
            }
        }

        return JSON.stringify({ ok: true, selector: 'none', error: null, items: [] });
    } catch (e) {
        return JSON.stringify({ ok: false, selector: null, error: String(e && e.message || e), items: [] });
    }
})();";
}

/// <summary>Result of a single Gmail DOM attachment probe.</summary>
public sealed class GmailVisibleAttachmentsDomResult
{
    public bool ExtractionSucceeded { get; init; }
    public System.Collections.Generic.IReadOnlyList<GmailVisibleDomAttachment> VisibleAttachments { get; init; }
        = Array.Empty<GmailVisibleDomAttachment>();
    public string? DomSelectorUsed { get; init; }
    public string? Error { get; init; }
    public bool WasSkipped { get; init; }
    public string? SkipReason { get; init; }

    public static GmailVisibleAttachmentsDomResult Skipped(string reason) => new()
    {
        ExtractionSucceeded = false,
        WasSkipped = true,
        SkipReason = reason,
    };

    public static GmailVisibleAttachmentsDomResult Failed(string error) => new()
    {
        ExtractionSucceeded = false,
        Error = error,
    };
}

/// <summary>A single attachment chip extracted from the Gmail DOM.</summary>
public sealed class GmailVisibleDomAttachment
{
    public string? FileName    { get; init; }
    public string? DisplaySize { get; init; }
    public string? AriaLabel   { get; init; }
    public string? Title       { get; init; }
    public string? InnerText   { get; init; }
    public string? DownloadUrl { get; init; }
    public string? SelectorPath { get; init; }
}
#endif
