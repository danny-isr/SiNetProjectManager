# DEV-004 / DEV-005 — Mark email as read, and «פתח ב-Gmail» for reply / forward

> **Title:** Email surface — read state and Gmail hand-off  
> **Date:** 03.08.2026  
> **Status:** Implemented on `development` (`SiNet.App.Wpf` 1.0.5) — pending PROD publish + operator verification  
> **Scope:** `SiNet.App.Wpf` email surface (`EmailDetailViewModel`, `EmailActionBarView`, `EmailListViewModel`) and the Gmail modify port (`IEmailGmailModifyService` / `GmailEmailModifyService`). Two operator gaps found in the PROD pilot.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) (Gmail label = mailbox truth), [`EMAIL_DETAIL_COMPONENT.md`](./EMAIL_DETAIL_COMPONENT.md), `SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md`

---

## 1. Why / product intent

1. **Read state (DEV-004).** Operators triage the mailbox inside the app, but Gmail never learns the message was read: the same mail stays bold in the app, in Gmail on the phone and in the browser. The mailbox is the source of truth for “seen”, so the app must write it — while keeping an escape hatch for anyone who is only skimming.
2. **Reply / forward (DEV-005).** The app has no reply path at all. Building an HTML composer, signatures, attachment handling and thread history inside the app is a large surface with a permanent maintenance cost. Gmail already does all of it, so the app hands the message over instead.

---

## 2. Existing mechanism (reuse — do not invent)

| Piece | Path | Current role |
| --- | --- | --- |
| Gmail modify port | `src/SiNet.Application/Abstractions/Email/IEmailGmailModifyService.cs` | Project labels + triage labels only |
| Gmail modify impl | `src/SiNet.Infrastructure.Google/GmailEmailModifyService.cs` | `ModifyMessageLabelsAsync(add, remove)` — already the single place that calls `Users.Messages.Modify` |
| Scopes | `src/SiNet.Infrastructure.Google/GmailClientProvider.cs` | `GmailModify` **already requested** — no consent change needed |
| Unread read-side | `GmailEmailGateway.ResolveIsUnread` → `EmailSummary.IsUnread` → `EmailListRow.IsUnread` | Bold row + blue bar in `EmailListItemCard.xaml`, `UnreadInCurrentPage` / `MailboxUnreadTotal` counters |
| RFC822 id | `GmailEmailGateway` header `Message-ID` → `EmailInboxMessage.InternetMessageId` (NOT NULL, UNIQUE) → `EmailListRow.InternetMessageId` | Already captured and persisted; no new data needed |
| Gmail URL in browser | `src/SiNet.Application/Email/QuoteSend/GmailComposeUrlBuilder.cs` | Quote send already opens Gmail in the browser — same pattern, proven |
| Browser launch | `EmailDetailViewModel.OpenInSystemBrowser` (private), `IAccResolvedDocsUrlLauncher` | `Process.Start` + `UseShellExecute` |
| Action bar | `src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarView(.xaml/ViewModel)` | «שייך לפרויקט» / «העבר לפרויקט» |
| DEBUG convention | `#if DEBUG` (`App.xaml.cs`, `NewShellFactory.cs`, `DevToolsServiceCollectionExtensions.cs`) | No `IHostEnvironment`-style service in this codebase |

**Gap:** nothing removes the `UNREAD` label, and nothing opens a specific message in Gmail. Legacy `SiNetSQL.MVVM.EmailManagementViewModel` only flipped `IsUnread` **locally**, relying on the fact that V2 displayed the message through a Gmail popout URL — the new system renders HTML locally, so that side effect is gone.

---

## 3. DEV-004 — mark as read

### 3.1 Target behavior

1. When the **body of a message finishes rendering successfully**, and the toggle is on, the app removes the `UNREAD` label from that Gmail message.
   - Trigger is the completed body load, **not** list selection: arrow-keying through the list must not burn through the mailbox.
2. The action bar gets a toggle: **«סמן כנקרא»**.
   - Default **on** in Release, **off** in Debug (`#if DEBUG` chooses the initial value only).
   - The operator can flip it in **both** builds; a Release operator can stop marking, a developer can switch it on to verify.
   - State lives in memory for the session (decided 03.08.2026). It resets to the build default on every launch. No new settings store.
3. Local state updates optimistically: `IsUnread = false` on the row plus the unread counters. If the Gmail call fails, the row reverts and the status line reports it — the mailbox stays the source of truth.
4. A message already read in Gmail is never re-modified (no call when `IsUnread` is already false).

### 3.2 Changes

| Layer | Change |
| --- | --- |
| `IEmailGmailModifyService` | New `Task MarkAsReadAsync(string gmailMessageId, CancellationToken ct)` |
| `GmailEmailModifyService` | Implement via the existing `ModifyMessageLabelsAsync(add: [], remove: ["UNREAD"])` |
| `EmailActionBarViewModel` | `MarkAsReadEnabled` toggle + build-dependent default |
| `EmailDetailViewModel` | After a successful body load, call the port when the toggle is on; optimistic local update with rollback |
| `EmailListViewModel` | Reuse the existing unread counters — no parallel counting |

### 3.3 Acceptance criteria

- [ ] Release build: opening a message clears bold in the app **and** in Gmail (verify in the browser). *(manual, PROD)*
- [ ] Debug build: opening a message leaves it unread until the operator turns the toggle on. *(manual, DEV)*
- [ ] Toggle off: no `Users.Messages.Modify` call is made. *(manual)*
- [ ] Gmail failure: the row returns to unread and the status line shows the error — no silent swallow. *(manual)*
- [ ] Fast arrow-key scrolling through the list does not mark messages whose body never rendered. *(manual)*
- [x] Automated: toggle default per build, and “no call when already read” (`EmailActionBarReadStateTests`; skip path when `!row.IsUnread` / toggle off in `TryMarkSelectedEmailAsReadAsync`).

### 3.4 As shipped (03.08.2026, `SiNet.App.Wpf` 1.0.5)

| Piece | Change |
| --- | --- |
| `IEmailGmailModifyService.MarkAsReadAsync` | Removes system label `UNREAD` via existing `ModifyMessageLabelsAsync` |
| `EmailActionBarViewModel.MarkAsReadEnabled` | Session toggle; default from `#if DEBUG` |
| `EmailDetailViewModel.TryMarkSelectedEmailAsReadAsync` | Runs after a successful body load in `RunSelectionPipelineAsync`; optimistic `PatchRowIsUnread` with rollback |
| `EmailListViewModel.PatchRowIsUnread` | Local row + exact mailbox unread total |

---

## 4. DEV-005 — «פתח ב-Gmail»

### 4.1 Target behavior

1. Every message in the detail surface gets a **«פתח ב-Gmail»** button in the action bar.
2. It opens the message in the default browser. The operator presses Reply / Reply all / Forward **inside Gmail**, which owns signatures, recipients, attachments, thread history and sending.
3. The app requests **no** send permission, builds **no** MIME, and stores **no** draft.

### 4.2 URL form (decided 03.08.2026)

Use the Gmail message id the app already holds on `EmailListRow.Id`:

```
https://mail.google.com/mail/u/<account>/#all/<gmailMessageId>
```

This opens the message itself — no intermediate search-results click. It is the form legacy V2 used successfully (`GoogleService.EmailInfo.GmailPopoutUrl`).

Two constraints to handle:

- **Account segment.** `u/0` means “first account signed into this browser”. When the app knows the connected Google account, prefer `u/<email>/`, which Gmail resolves explicitly. Otherwise fall back to `u/0/` — a documented limitation, not a silent guess.
- **Not an official API.** Gmail UI URL routing is not a contract Google guarantees. The officially supported alternative is the `rfc822msgid:<Message-ID>` search (the app already stores `InternetMessageId` for it, and the legacy connector already resolves ids that way in `ResolveLocalMessageIdByRfc822Async`). If Google ever breaks `#all/<id>`, switching to search is a one-line change in the URL builder — that is why the URL must be built in **one** place.

### 4.3 Changes

| Layer | Change |
| --- | --- |
| `SiNet.Application/Email` | New `GmailMessageUrlBuilder` next to the existing `GmailComposeUrlBuilder` — pure, testable, single place the URL is formed |
| `EmailActionBarViewModel` | `OpenInGmailCommand`, enabled only when a message is selected |
| `EmailActionBarView.xaml` | «פתח ב-Gmail» button |
| `EmailDetailViewModel` | Route through the existing `OpenInSystemBrowser` — no new launcher service |

**Guard to respect:** `EmailDetailBoundaryTests` asserts the viewer XAML contains no `mail.google.com`. That guard is about **rendering the body** through a Gmail popout, which must not come back. A browser launch from the action bar does not violate it, and the boundary test must keep passing untouched.

### 4.4 Acceptance criteria

- [ ] Button opens the correct message in the browser, in the right account. *(manual, PROD)*
- [ ] Reply / Reply all / Forward inside Gmail behave normally (signature, thread, attachments). *(manual)*
- [x] No new OAuth scope is requested and no consent prompt appears. (`GmailModify` / existing scopes unchanged)
- [x] Body rendering still never navigates to `mail.google.com`; the boundary test is unchanged and green.
- [x] Automated: URL builder output, and behavior when the message id is missing (`GmailMessageUrlBuilderTests`).

### 4.5 As shipped (03.08.2026, `SiNet.App.Wpf` 1.0.5)

| Piece | Change |
| --- | --- |
| `GmailMessageUrlBuilder` | `#all/{gmailMessageId}` with `u/<email>` when `ConnectedAccountEmail` is known, else `u/0` |
| Action bar | «פתח ב-Gmail» → `OpenInSystemBrowser` |
| Viewer XAML | Still contains no `mail.google.com` — body stays local HTML |

---

## 5. Out of Scope

- An in-app HTML composer, signatures, attachment picker or send path.
- Gmail API draft creation (`users.drafts.create`, `gmail.compose`) with `threadId` / `In-Reply-To` / `References`. Deferred — see §7. Note `GmailSend` is already in the scope list, so this stays open as a later step.
- “Mark as unread” as a manual action, bulk mark-as-read, and read state for the calendar surface.
- Changing ACC / Gmail / DB source-of-truth rules.
- Persisting the mark-as-read toggle across launches (see §6).

## 6. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| `#if DEBUG` around the whole mark-as-read behavior | Rejected 03.08.2026 | Release could not switch it off and Debug could not verify it; the build now only picks the default |
| Persisted (cross-launch) toggle state | Postponed 03.08.2026 | Session-scoped first; revisit only if operators complain about the daily reset |
| Marking as read on list selection | Rejected | Arrow-key scrolling would mark everything the operator never read |
| `rfc822msgid:` search as the primary open path | Postponed | Officially supported, but costs an extra click; kept as the documented switch if `#all/<id>` breaks |
| Gmail API reply draft | Postponed | Needs MIME building + thread headers; no operator demand yet |

## 7. Needs Review

- ~~Does the app expose the connected Google account address to the UI layer?~~ Resolved — `EmailListViewModel.ConnectedAccountEmail` is used by `GmailMessageUrlBuilder`.
- Should «פתח ב-Gmail» also appear on the email list row context menu, or is the action bar enough?
- Should the mark-as-read toggle be per-surface (main shell vs the pop-out work-item window) or global to the session? *(shipped: per action-bar instance / per window)*
