# Startup ops alerts — development directive

> **Title:** Startup ops alerts for the primary admin (port V2 SyncFailures + AccService token gaps)  
> **Date:** 03.08.2026  
> **Status:** Planning  
> **Scope:** Product/engineering directive for the **`development`** branch. **Do not implement on `release` / PROD in this round.** Describes what App.Wpf must show when the primary admin opens the shell, reusing the V2 pattern and extending it to AccService / sync / anomalous ops signals.

Related: [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md),
[`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md),
[`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md),
[`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md),
[`ENVIRONMENTS.md`](./ENVIRONMENTS.md),
[`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md) (DEV-027: classify 401 vs TLS in the status panel; employee import — complements this admin popup, does not replace it).

---

## 1. Why

On 03.08.2026 the PROD pilot hit AccService without an Autodesk refresh token (`refreshTokenFileExists=false`). JumboMail downloaded files but hung on ACC upload; NativeAccIngest timed out at 100s. The admin had to discover this from log files.

V2 Legacy already had an **admin-only, non-modal** popup of recent `Sync_RunFailures` on startup (`ShowSyncFailureAlertIfAdmin` → `SyncFailuresWindow`). App.Wpf has a footer + on-demand «מצב מערכת» but **no automatic popup**. That gap is unacceptable for the primary operator during pilot.

---

## 2. Existing mechanism (reuse, do not reinvent)

| Mechanism | Where | Reuse |
| --- | --- | --- |
| V2 sync failure popup | `SiNetProjectManagerV2/App.xaml.cs` `ShowSyncFailureAlertIfAdmin`; `Dialogs/SyncFailuresWindow` | Port the **behavior** (admin-only, non-modal, last N days of `Sync_RunFailures`) into App.Wpf — do not resurrect the V2 host |
| Footer health + System Status | `NewShellWindow` footer; `SystemStatusWindow`; `IRuntimeSubsystemStatusService` | Keep; alerts should deep-link / open this window |
| AccService / Autodesk contributors | `AccServiceStatusContributor`, `SystemStatusGuidanceCatalog` | Extend with an explicit "refresh token missing" signal when diag exposes it |
| Token refresh ops | [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) | Guidance text in the alert must point here |

---

## 3. Target state (development)

### 3.1 Who sees alerts

- Primary admin / full-access operators only (same idea as V2 `IsFullAccess`).
- Non-admin users: no popup; footer may still show degraded state without blocking.

### 3.2 When

- Once per application session, shortly after NewShell is ready (background, non-blocking) — same timing spirit as V2 `Dispatcher.BeginInvoke(..., Background)`.
- Optional: re-check when the user returns focus after a long idle (postponed unless cheap).

### 3.3 What must raise an alert (minimum set)

| Signal | Source | Operator action hint |
| --- | --- | --- |
| Recent SyncEngine failures | `SiData.dbo.Sync_RunFailures` (e.g. last 7 days, capped list) | Open details; fix CrossSync / rate limits / hours watermarks per monitoring docs |
| AccService unreachable / TLS pin mismatch | Existing AccService health contributor | Fix BaseUrl / pinned thumbprint |
| **AccService Autodesk refresh token missing / stale** | AccService startup diag or a small authenticated `/diag` field (`refreshTokenFileExists`) | Follow [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) |
| Gmail session unavailable | Existing Gmail contributor | Re-auth Google |
| Repeated `[NativeAccIngest]` / ACC upload timeouts in the **current** session | Client log or ingest tracker (if already exposed to UI) | Check AccService token / Autodesk |

"Anomalous logs" in general are **not** a free-form log tail in the UI. Prefer **structured health signals** already (or easily) available to the runtime status service. Central UNC log scraping from the client is out of scope unless explicitly approved later.

### 3.4 UX

- Non-modal floating window or toast list (V2 `SyncFailuresWindow` is the reference).
- Does not block opening mail / work.
- Each item: short Hebrew summary + "פתח מצב מערכת" / "העתק הנחיה".
- Dismiss for this session; do not permanently hide unresolved Sync_RunFailures without an explicit "סמן כטופל" (optional follow-up).

### 3.5 Implementation notes (for the DEV agent)

1. Document-first: update this file + [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md) §gap before coding.
2. Prefer extending `IRuntimeSubsystemStatusService` / contributors over a parallel alert bus.
3. Expose `refreshTokenFileExists` (or equivalent) from AccService `/v1/acc/diag` if not already present — client must not read `sieng`'s disk.
4. Port query shape from V2 `Sync_RunFailures` read; keep one behavior per test.
5. Ship on **`development`** first; absorb into `release` only after PROD operator acceptance.

---

## 4. Out of Scope

- Implementing the feature on the `release` branch / PROD publish in the 03.08.2026 ops round
- Email/SMS/PagerDuty paging
- Scraping central Serilog files from App.Wpf as the primary signal
- Device Auth for Autodesk
- Changing AccService to run interactive browser itself while hosted as a service

## 5. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Blocking MessageBox on every startup | Dropped | V2 used non-modal; blocking hurts pilot usability |
| Alert every Warning in Client log | Postponed | Too noisy at Warning; use structured health first |
| Auto-heal / auto-open browser as sieng | Dropped | Service account cannot own an interactive browser session safely |

## 6. Needs Review

- Exact admin gate in App.Wpf (`Administrator` role vs a dedicated ops flag).
- Whether `/v1/acc/diag` already returns token-file presence (Needs Review against AccService `AccEndpoints`).
- Retention / "mark handled" for Sync_RunFailures rows.
