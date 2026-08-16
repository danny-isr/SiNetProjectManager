# Production Monitoring — Live logs and process health

> **Title:** Production Monitoring  
> **Date:** 02.08.2026  
> **Updated:** 16.08.2026 (Client heartbeat lock; AccService token restored)  
> **Status:** Active  
> **Scope:** How the PROD workstation watches real-time logs and subsystem / workflow health during the pilot. Complements architecture docs; does not replace them.

Related: [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md), [`LOGGING.md`](./LOGGING.md), [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md), [`OPS_LLOG_REVIEW.md`](./OPS_LLOG_REVIEW.md) (Cursor incremental Llog sweep), [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) (severe token notice), [`SiNetProjectManagerV2/Docs/LOGGING.md`](../SiNetProjectManagerV2/Docs/LOGGING.md).

---

## 1. Purpose

During the first real-user period, the PROD machine’s second job (after publishing) is **ops**:

1. Detect crashes and unhandled exceptions quickly.  
2. Confirm subsystems (SQL, ACC, Gmail, AccService, …) stay healthy.  
3. Watch workflow instances that stall or fail recovery.  
4. Escalate with enough context (machine, user, time, message).

There is **no** in-app live log tail in `SiNet.App.Wpf`. Monitoring uses the **UNC central Serilog share**, local files when needed, and two admin UI surfaces.

---

## 2. Log map

Default central root: `\\si-win-2k19\AutoCAD Data\log`  
(overridable via SystemSettings `Logging.CentralLogPath` on the DB the process uses).

Layout:

```text
{CentralLogPath}\{AppName}\{MachineName}\{UserName}\{AppName}-yyyyMMdd.log
```

| Process | App folder / file prefix | Local default |
| --- | --- | --- |
| `SiNet.App.Wpf` (production desktop) | `Client\<machine>\<user>\Client-yyyyMMdd.log` | `%LOCALAPPDATA%\SiNet\Logs\Client-yyyyMMdd.log` |
| `SiOffice.AccService` | `AccService\...` | `%ProgramData%\SiOffice\AccService\logs\` |
| `MasterPlan.SyncEngine` | `SyncEngine\...` | `%ProgramData%\SiOffice\MasterPlanSync\logs\` |

Enrichers on desktop lines include `App`, `Host=SiNet.App.Wpf`, `Machine`, `User`, `ProcessId`, `ThreadId`. There is **no** `Environment` property yet — see [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) §6.1.

### 2.1 Levels that matter for ops

| Sink | Default min level (Client) | Controlled by |
| --- | --- | --- |
| Local file | User toggle: Debug when `LoggingEnabled=true`, else effectively silenced via level switch | Per-user settings |
| Central UNC | **Warning** (`Logging.Client.CentralLevel`) | Admin SystemSettings |

**Important:** 1.0.32 writes Warning `[STARTUP] Client process alive` **locally**, but PROD Llog on 16.08 stayed empty even when `CentralEnabled=true`. Folder probe ≠ share file. Until [`DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md`](./DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md) (DEV-028) ships, a quiet Client folder is **not** proof of idle. After that ship: heartbeat must be readable on the UNC from this ops PC.

**Pilot decision (02.08.2026):** keep **`Logging.Client.CentralLevel` = Warning** (default). Do **not** lower it to Information. Heartbeat is Warning in code, not an ops level change.

Per-user `LoggingEnabled=false` must **not** silence the central sink (design in [`LOGGING.md`](./LOGGING.md) §9).

### 2.2 Agent incremental review (Cursor)

Do **not** list “today only” by hand. Use [`OPS_LLOG_REVIEW.md`](./OPS_LLOG_REVIEW.md) / skill `llog-review`: byte cursor in `artifacts/llog-review/state.json`, findings in `ledger.json`. Old files absent from state are unread (that is how a June Lilach file is still in scope).

---

## 3. Live tail commands (PowerShell)

Replace `<Machine>` / `<User>` with the pilot PC name and Windows user. Date segment is `yyyyMMdd`.

### 3.1 Central — desktop client (recommended for pilot)

```powershell
$day = Get-Date -Format yyyyMMdd
$root = '\\si-win-2k19\AutoCAD Data\log\Client'
# List today's files across machines:
Get-ChildItem $root -Recurse -Filter "Client-$day.log" -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 20 FullName, Length, LastWriteTime

# Tail one specific user (example):
Get-Content "\\si-win-2k19\AutoCAD Data\log\Client\<Machine>\<User>\Client-$day.log" -Wait -Tail 80
```

### 3.2 Local on the PROD workstation (when investigating this machine)

```powershell
$day = Get-Date -Format yyyyMMdd
Get-Content "$env:LOCALAPPDATA\SiNet\Logs\Client-$day.log" -Wait -Tail 80
```

Enable verbose local logging for a user via Settings (`LoggingEnabled`) if the local file is quiet.

### 3.3 AccService

```powershell
$day = Get-Date -Format yyyyMMdd
Get-Content "\\si-win-2k19\AutoCAD Data\log\AccService\<ServerMachine>\<ServiceUser>\AccService-$day.log" -Wait -Tail 80
# Local on the service host:
Get-Content "$env:ProgramData\SiOffice\AccService\logs\AccService-$day.log" -Wait -Tail 80
```

### 3.4 Optional workflow debug file (opt-in)

Env `SINET_WF_DEBUG=1` writes  
`%LOCALAPPDATA%\SiNet\SiNet.App.Wpf\Logs\workflow-manual-debug.log` — use only when reproducing a workflow bug; not a general monitoring channel.

---

## 4. What to look for

| Signal | Typical text / place | Severity | Action |
| --- | --- | --- | --- |
| UI / global exception | `[UI]` / “Reported error” from `AppGlobalExceptionHandling` → Warning | High | Note machine/user/time; reproduce; fix or rollback |
| Fatal startup | `Fatal` after vault/schema failure | Critical | User cannot start — fix vault/SQL/schema |
| Central sink down | `[STARTUP] Central log sink unavailable` | High | Share permissions / path / probe failure |
| Schema gate | Database schema validation failure at startup | Critical | Do not force users forward; operator schema path |
| AccService unreachable | System Status row `acc-service` / ACC errors in log | High | Service health, TLS pin, API key |
| Auth / Gmail | Rows `gmail`, `google-account`, Google errors | Medium–High | Token store, OAuth secrets |
| Workflow stall | Ops dashboard instance age / failed recovery | Medium–High | Advance / retry / cancel per policy |

Handlers (desktop): `AppGlobalExceptionHandling` covers `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Reports are bridged to Serilog as Warning with `[UI]` context from the host.

---

## 5. In-app health surfaces

### 5.1 «מצב מערכת» (System Status)

| Item | Detail |
| --- | --- |
| Service | `IRuntimeSubsystemStatusService` / `RuntimeSubsystemStatusService` |
| UI | `SystemStatusWindow` + footer indicator on NewShell |
| Refresh | Periodic ~ every **5 minutes** (plus manual refresh) |
| Built-in / contributor rows | ACC, Gmail, ingest, workflow-assignees, database, workflow, seed-baseline, file-server, Ollama, Google config/account/folders, Autodesk token, AccService, … |

Authoritative row catalogue and guidance: [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md).

**When to open:** any pilot complaint, after every publish, and at start of day during the first week.

### 5.2 «בריאות תהליכים» (Workflow Ops Dashboard)

| Item | Detail |
| --- | --- |
| UI | `WorkflowOpsDashboardView` / Window |
| Permission | Administrator feature `Shell.OpenWorkflowOpsDashboard` (+ action-specific codes) |
| Refresh | About every **20 seconds** |
| Role | Runtime instance health and controlled advance / pause / cancel / retry / start |

Authoritative behavior: [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md).

Complements System Status — does **not** replace infrastructure checks.

### 5.3 «דוח קריסות תחנה» (Workstation crash report)

| Item | Detail |
| --- | --- |
| UI | `WorkstationCrashReportWindow` under **מנהלה** |
| Permission | Any signed-in user (no feature code) |
| Source | Local Windows Event Log — `Application` (Civil 3D / acad crashes) + `System` (bugcheck, power, WHEA, disk) |
| Output | CSV + Markdown for offline AI analysis, saved per machine under the configured share |

Use it when a user reports repeated Civil 3D crashes or a machine that reboots on its own. It is about **the workstation**, not about SiNet's own logs. Authoritative behavior: [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md).

### 5.4 Watchdog note

`StalledWorkflowWatchdog` exists in the New System SQL infrastructure, but an **automatic background loop** is historically wired on the legacy V2 host. On standalone `SiNet.App.Wpf`, treat recovery as **operator-driven** via the Ops Dashboard / `IWorkflowRecoveryService` until a dedicated background host loop is approved and documented.

---

## 6. Monitoring routines

### 6.1 Start of day (pilot week)

1. Confirm AccService is running on the server.  
2. Open `SiNet.App.Wpf` on PROD (or a pilot PC) → «מצב מערכת» — no Unexpected/Failed critical rows.  
3. Tail central `Client` logs for overnight `Fatal` / `[UI]` / `Central log sink unavailable`.  
4. Skim Workflow Ops for instances stuck longer than the office’s agreed SLA (define operationally; default attention: multi-hour stalls).

### 6.2 After every release

1. One pilot (or PROD) machine launches the app — confirm MSIX updated if expected.  
2. System Status all green / Idle acceptable.  
3. Tail that machine’s central log for 5–10 minutes of normal use.  
4. Record blockers in the rollout sign-off table: [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md).

### 6.3 Escalation

| Severity | Who | Artifact to keep |
| --- | --- | --- |
| User cannot start | Operator on PROD | Local + central log excerpt, schema/vault error text |
| Widespread `[UI]` after publish | Operator → consider rollback ([`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §7) | Version on share, time window, sample stack |
| Single-user ACC/Gmail | Support | System Status row key + user machine name |

---

## 7. Monitoring gaps (documented; not fixed in this docs round)

| Gap | Impact | Recommended follow-up |
| --- | --- | --- |
| Central default level Warning; Client startup was Information | Quiet Client share; emptied stations look idle | **Locked 16.08:** Warning heartbeat every start — [`LOGGING.md`](./LOGGING.md) §9.4.1. Keep central at Warning |
| OS title shows version (As-Is) | Operators can confirm build from window title; UNC/MSIX still useful | Version comes from `NewShellWindowTitle` / assembly informational version -- optional About polish only |
| No `Environment` enricher | DEV noise on same share hard to filter | [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) §6.1 |
| No in-app log viewer | Operators need PowerShell / Explorer on UNC | Acceptable for pilot; optional later |
| Standalone stalled-workflow background loop | Relies on human Ops Dashboard | Decide whether to host watchdog in App.Wpf or a service |
| OPS-P0 backup / secret rotation still Manual Pending | Blind recovery | Complete [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md) and [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) before wide rollout |
| No severe user notice when AccService Autodesk token is missing | User retries ACC; ops finds it only in Llog | [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) lock 16.08 — Critical for **every** ACC user; restore via [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) |
| AccService Autodesk `refreshTokenFileExists=false` (16.08 12:19 after restart) | EnsureInbox `Authorization was cancelled`; material cannot transfer | Token file still not in `sieng` LocalAppData — finish Export+Install and confirm `refreshTokenFileExists=true` |

---

## 8. Out of Scope

- Building a Seq/HTTP telemetry pipeline  
- Implementing an in-app log viewer  
- Changing default log levels in code or in production SystemSettings for the early pilot  
- Wiring automatic `StalledWorkflowWatchdog` into the standalone host  

## 9. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Relying only on local logs with `LoggingEnabled=false` | Dropped as ops strategy | Central sink is the ops channel |
| Lowering `Logging.Client.CentralLevel` to Information for early pilot | Postponed / declined for now | Owner decision 02.08.2026 — keep Warning |
| Separate DEV central log root for early pilot | Postponed | Not needed yet |
| Migration POC “log window” as production viewer | Not promoted | Not a supported pilot tool |
| Full parity with legacy SystemHealthService 11-check set inside this doc | Deferred to [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md) | Avoid duplicating the row catalogue |

## 10. Needs Review

1. Agreed SLA for “stuck” workflow instances before mandatory operator action.  
2. Revisit central level / DEV log root only if pilot noise or silence becomes a real problem.
