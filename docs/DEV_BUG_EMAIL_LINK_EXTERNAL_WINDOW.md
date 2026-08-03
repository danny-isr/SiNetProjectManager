# DEV-001 — Email body links must open external download window (Jumbo → ACC)

> **Title:** In-body hyperlink navigation bypasses Jumbo/WeTransfer → ACC pipeline  
> **Date:** 03.08.2026  
> **Status:** Open (implementation on `development`)  
> **Scope:** `SiNet.App.Wpf` email detail WebView2 body: click handling for file-transfer URLs (JumboMail, WeTransfer, and other hosts already detected by `EmailExternalDownloadLinkDetector`). Documentation-first bug + acceptance for a versioned desktop fix.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) · Related: [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) (N2 Jumbo), [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md)

---

## 1. Symptom (PROD pilot 03.08.2026)

1. Operator opens an email that contains a **JumboMail** (or similar) link in the HTML body.
2. Clicks the link **inside the email body**.
3. JumboMail loads **inside the same email WebView2 pane** (in-place navigation).
4. Downloads / site UX happen in that pane — **not** in `ExternalDownloadBrowserWindow`.
5. Therefore the intended pipeline (download to temp → upload to ACC Inbox → status “הושלם”) does **not** run from that click path.

Workaround today: use the **link chip** on the attachment strip (if shown), which calls `OpenExternalDownloadLink` → dedicated window.

---

## 2. Existing mechanism (reuse — do not invent a parallel downloader)

| Piece | Role |
| --- | --- |
| `WebView2EmailBodyRenderer` | Renders HTML body via `NavigateToString`; **no** `NavigationStarting` / `NewWindowRequested` handlers today |
| `EmailExternalDownloadLinkDetector` | Knows Jumbo / WeTransfer / Drive-style hosts |
| `EmailDetailViewModel.OpenExternalDownloadLink` | Chip path → `EmailExternalDownloadHandler` |
| `WpfEmailExternalDownloadBrowserHost` + `ExternalDownloadBrowserWindow` | Separate WebView2 window; intercepts downloads → temp → ACC upload (no V2 association dialog) |
| `EmailExternalDownloadHandler` / coordinator / `NativeEmailExternalDownloadExecutor` | ACC Inbox upload + DB helper row (`IsExternalDownload`) |

**Gap:** body hyperlinks never enter the chip / host path; WebView2 follows the URL in-place.

---

## 3. Target behavior

1. Clicking a link in the email body **must not** navigate the body WebView2 to an external site for file-transfer hosts (and preferably for any non-cid / non-mailto navigation).
2. If `EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url)`:
   - Cancel in-body navigation.
   - Open `IEmailExternalDownloadBrowserHost.OpenDownloadUrl` (same as chips) with the current email context.
3. Inside that window: user completes Jumbo/WeTransfer → file downloads → app uploads to **ACC** (physical file SoT) → progress to Completed/Failed.
4. Optional (same slice if cheap): other http(s) links open in the **system browser** or a generic external window — **not** inside the email body. Confirm with product; minimum for this bug is file-transfer hosts only.
5. Chips remain as a secondary entry; behavior must stay consistent.

### Acceptance criteria

- [ ] Click JumboMail link in HTML body → `ExternalDownloadBrowserWindow` opens; body still shows the email.
- [ ] Download in that window → ACC Inbox upload succeeds (with healthy AccService token).
- [ ] Regression: cid images / mailto still work; chip path still works.
- [ ] Automated test for navigation cancel + detector routing (behavior name per testing rules).
- [ ] Desktop package version bumped; published via normal `publish-desktop` / `publish-all` on PROD machine after DEV merge absorb — operator verifies appinstaller update.

### Version / release request

- Ship as a **normal App.Wpf version bump** on `development`, then absorb into `release` and publish from PROD per [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) / [`ENVIRONMENTS.md`](./ENVIRONMENTS.md).
- Use this incident to **exercise the update channel** (appinstaller OnLaunch) on the PROD workstation after publish.

---

## 4. Suggested implementation notes (for DEV agent)

1. In `WebView2EmailBodyRenderer.EnsureInitializedAsync` (or first CoreWebView2 ready), subscribe to:
   - `NavigationStarting` and/or `NewWindowRequested`
2. On external URL: `e.Cancel = true` (or set `Handled` on NewWindowRequested), then raise a callback / inject `Action<string> onExternalLink` registered from `EmailDetailViewModel`.
3. Prefer **one** shared open path with chips (`OpenExternalDownloadLink`).
4. Do not scrape UNC logs; do not add a second download executor.
5. Docs-first: keep this file Updated when behavior ships; mark DEV-001 Done in [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

Complexity: **Low–Medium** (WebView2 event wiring + DI callback). Risk: breaking in-body anchors or image loads if cancel is too broad — guard with detector / scheme checks.

---

## 5. Out of Scope

- Reintroducing V2 “associate to project” dialog
- Changing ACC vs Gmail vs DB source-of-truth rules
- AccService token tooling (already separate ops docs)
- Implementing the fix on the PROD machine / `release` without DEV cycle

## 6. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Rely on chips only (no body-click fix) | Rejected for pilot | Operators click the visible link in the body |
| Always system browser for Jumbo (no ACC upload) | Rejected | Breaks Inbox filing SoT |

## 7. Needs Review

- Exact list of hosts beyond detector defaults (any new Jumbo domains).
- Whether **all** http(s) body links should leave the email WebView, or only detector matches.
