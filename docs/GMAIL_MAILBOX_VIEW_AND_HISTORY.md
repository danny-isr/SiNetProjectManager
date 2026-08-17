# Gmail mailbox view preferences + History lightweight detection

> **Title:** Gmail Mailbox View & History (New System)
> **Date:** 17.08.2026
> **Status:** Active
> **Scope:** New System Email Workbench only (`EmailListViewModel` / `GmailEmailGateway` / `EmailMailboxQueryComposer`)
> **Related:** [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md), [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md)

## Goal

1. **View preferences (per user):** separate **Scope**, **Category**, and **Unread state** for the mailbox list; persist in `UserSetting`; compose Gmail queries in one place (`EmailMailboxQueryComposer`).
2. **Lightweight change detection:** poll Gmail History every 3 minutes for `messageAdded` only; on relevant new mail, trigger the existing full-page reload through a shared single-flight gate — no Pub/Sub, no incremental list injection.

## Existing mechanisms (reuse)

| Mechanism | Location | Role |
| --- | --- | --- |
| Query composition | `EmailMailboxQueryComposer` | Single `BuildSearchQuery` / unread-count builder |
| List load | `EmailListPagingCoordinator.LoadMailboxAndProjectAsync` | Full page load |
| Open refresh gate | `EmailWindowViewModel._autoRefreshGate` | Extended to shared `_reloadGate` + `ReloadPending` |
| Gmail API | `GmailEmailGateway` + `GmailClientProvider` | Add History.list / Profile.HistoryId |
| User prefs storage | `UserSetting` (SQL) | New columns for Scope/Category/UnreadOnly |

## Principles — Scope / Category / State

| Layer | Values | Gmail effect |
| --- | --- | --- |
| **Scope** | `Inbox`, `AllMail` (UI). Also `Label`, `Sent` for existing flows. | Inbox → `label:INBOX`. AllMail → `-in:spam -in:trash -in:drafts -in:sent` (unchanged). |
| **Category** | `All`, `Primary`, `Updates`, `Promotions`, `Social`, `Forums` | When not `All`, append `category:<name>`. Default **All** (no category clause). |
| **State** | `UnreadOnly` bool | When true, append `is:unread` only. |

**Default (no prefs / empty):** Scope=`Inbox`, Category=`All`, UnreadOnly=`false` → `label:INBOX`.

**`EmailMailboxScope.Unread`:** removed from UI; retained as `[Obsolete]` compatibility. Any use maps to Scope=`Inbox` + `UnreadOnly=true`. Deletion deferred until after verification.

**`MailboxViewQuery` ≠ `MailboxHistoryCheckpoint`:** filter/search query results must never move the History checkpoint.

## Principles — History detection (v1)

### Baseline (full sync / init / history expired)

```text
1. Capture baseline = users.getProfile().HistoryId   // BEFORE mailbox load
2. Perform mailbox load
3. On success → Commit LastHistoryId = baseline
4. Mail arrived during load stays AFTER baseline → next history.list returns it
5. Prefer one harmless duplicate refresh over silently missing mail
```

Do **not** commit checkpoint from the first message of a view/query result. Do **not** use empty-list → getProfile after list (race).

### Poll (every 3 minutes)

- `users.history.list` with `historyTypes=messageAdded` only.
- Scope=`Inbox` → also `labelId=INBOX`. Scope=`AllMail` → no `labelId`.
- **Must page all `nextPageToken`s** before advance/commit or deciding that `messagesAdded` occurred.
- Session-only: `LastHistoryId`, `PendingHistoryId`, `ReloadPending` — **not** persisted to DB.

### Checkpoint vs reload (transactional)

| Outcome | Action |
| --- | --- |
| No relevant `messagesAdded` | Advance `LastHistoryId` immediately to response `historyId`. |
| Relevant `messagesAdded` | Store `PendingHistoryId` (max); request reload via shared gate. **Do not** advance `LastHistoryId` yet. |
| Reload succeeds | Commit `LastHistoryId = PendingHistoryId`; clear pending. |
| Reload gate busy | Set `ReloadPending=true`; keep highest `PendingHistoryId`; after active reload completes, run **one** coalesced follow-up reload; commit only after that succeeds. |
| Reload fails | Keep `PendingHistoryId`; do not advance `LastHistoryId`. |
| Transient History/API error | Log; leave checkpoint/pending unchanged; retry next poll cycle. |
| HTTP 404 history expired | Only this path re-enters baseline-before-sync + full reload. |

**Critical:** never advance the checkpoint when a reload for `messagesAdded` was skipped — that permanently consumes the event before the UI synced.

### Reload single-flight

All full loads (Manual Refresh, Scope/Category/Unread, Search, History detection, AutoRefreshOnOpen, history-expired baseline) go through the **same** `_reloadGate` / `ReloadPending` orchestration. Not `IsBusy` alone. No second History-only lock. No queue of reloads — one pending flag is enough.

### Ordinary UI reloads

Scope / Category / Unread / Search reloads **do not** derive or reset `LastHistoryId` from query results.

## Persistence (`UserSetting`)

| Column | Meaning |
| --- | --- |
| `GmailMailScope` | nvarchar(32) — `Inbox` / `AllMail` |
| `GmailMailCategory` | nvarchar(32) — `All` / `Primary` / … |
| `GmailUnreadOnly` | bit, default false |

`LastHistoryId` is **not** stored in DB.

**Migration (user-owned):** after model change, run from Package Manager Console / CLI (do **not** let the agent create migration files):

```powershell
dotnet ef migrations add AddUserSettingGmailMailViewPrefs --context SiNetSQLDbContext --project src\SiNet.Infrastructure.Sql\SiNet.Infrastructure.Sql.csproj --startup-project SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
```

Then apply when ready (`dotnet ef database update --context SiNetSQLDbContext ...`).

## Logging

Material History decisions use structured logs with prefix `[GmailHistory]` (advance, pending, reload request, coalesced follow-up, commit, transient failure, expired).

## Out of Scope

- Pub/Sub / `users.watch`
- Incremental message injection into the ObservableCollection
- DB persistence of History checkpoint
- Legacy `EmailManagementViewModel` / `GoogleService` parity in this round
- Full `IGmailThrottleService` wiring to the native gateway
- Deleting `EmailMailboxScope.Unread` enum value

## Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Full Load → getProfile → LastHistoryId | Cancelled | Race window after load |
| Checkpoint from newest listed message after every UI reload | Cancelled | View query ≠ mailbox state; can regress |
| Empty list → getProfile commit | Cancelled | Not race-safe |
| Reload on labelAdded / labelRemoved / messageDeleted | Postponed (v1) | Detection goal is new mail only |
| Always advance HistoryId after history.list even when reload skipped | Cancelled | Can drop mail until manual Refresh |
| Hard-delete Scope.Unread | Postponed | Compatibility until tests pass |
| Legacy Primary unread badge alignment | Postponed | Separate round |

## Needs Review

- Migration apply on PROD/DEV after model change (agent does not run EF migrations).
- Optional later: false-positive AllMail reloads from Sent `messageAdded` without `labelId`.
