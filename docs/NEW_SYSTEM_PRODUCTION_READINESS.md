# New System — Production cutover envelope (`SiNet.App.Wpf`)

> **Status:** Active (2026-08-02) -- **cutover host** (replaces V2 distribution)
> **Updated:** 07.08.2026 (As-Is reconciliation -- evidence layers; §8.1 Historical; selector not in header)
> **Scope:** Defines what **`SiNet.App.Wpf.exe`** may expose as the **only shipped desktop app**.
> V2 remains in-repo for reference/build; it is **not** published. Office safety net until
> cutover sign-off is the external legacy system (outside this repo).
> This is still **not** approval of GmailSend / Reply / Forward (G-Policy still open).
>
> Locked host decision: Production desktop = `SiNet.App.Wpf.exe` only.
> See [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md), [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md),
> [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md).
>
> Related:
> [`TEST_STRATEGY.md`](./TEST_STRATEGY.md),
> [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md),
> [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md),
> [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md),
> [`manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md`](./manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) (G-Policy = Send/Reply/Forward only),
> [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md),
> [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md),
> [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md),
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md).

---

## 0. Evidence layers (do not conflate)

| Layer | Meaning | Example |
| --- | --- | --- |
| Repo tip | What `origin/release` / `origin/development` contain | Ship commit `127dc0e`, App.Wpf **1.0.23** (verified 2026-08-07 follow-up; earlier reconciliation snapshot was `3bfe152` / 1.0.22) |
| Automated tests | What CI / local `dotnet test` proved on a given commit | §8.1 Historical Snapshot |
| Interactive smoke | Operator checklist on a machine | §9 -- **Not Run** unless signed |
| Ops install | What pilot PCs actually run from UNC | [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) -- **Needs Review** |
## 1. Purpose & status

`SiNet.App.Wpf` (`AddSiNetStandaloneHost` / `SiNetHostMode.StandaloneNew`) is the **production New
System host** for a controlled internal pilot when:

- Vault + SQL schema gates and Windows-user auth succeed at startup.
- Gmail silent restore runs via `IConnectorAuthService.TryRestoreSessionAsync`.
- Shell menu exposes only **feature-gated** native surfaces (no DEBUG harness in Release).
- Email is an **ACC-filing pilot** (N1–N3), not a send/reply client.
- AccService **Remote** is the default MultiStart path (`AccService:BaseUrl`); Local inbox bootstrap
  is available only when BaseUrl is empty (slice 2b).

**Interactive smoke status:** **Not Run** — see §9. Agent/build/tests do **not** authorize pilot users.

### Controlled Production Pilot runtime (P1)

Fail-closed root-start controls are documented in [`PILOT_CONTROLS.md`](./PILOT_CONTROLS.md):

- SystemSettings: `Pilot.Enabled`, `Pilot.AllowedUserIds`, `Pilot.AllowedWorkflowCodes` (absent → deny)
- Gate: `NativeWorkflowCommandService.StartAsync` (Email / Ops Start / System continuation)
- Children under a parent bypass the root gate
- `QuoteApprovedByClient` pre-validates required continuations with **`command.UserId`** before mutate

**Ops must not enable allowlists until after code review and live smoke.** Defaults keep all new root starts blocked.

**Operational risk (documented, not fixed in P1):** if System Settings Load fails before Pilot fields are applied and an admin still Saves, fail-closed defaults (`Pilot.Enabled=false`, empty allowlists) may be written. Normal Loaded→Load→Save preserves Pilot values (covered by Settings surface regression test).

---

## 2. Host model

| Process | Role |
| --- | --- |
| **`SiNet.App.Wpf.exe`** | **Production desktop app** (MSIX channel 3/4) |
| **`SiNetProjectManagerV2.exe`** | In-repo reference / hybrid build only — **not published** |
| External legacy system | Safety net until cutover sign-off (outside this repo) |

Composition: `AddSiNetStandaloneHost` → `AddSiNet(StandaloneNew)` + vault SQL + native WPF surfaces.
Launch: `dotnet run --project src/SiNet.App.Wpf`, or VS MultiStart **New System + AccService**.

---

## 3. Allowed production surfaces & feature gates

Implemented in `NewShellFactory.BuildMigratedOnlyMenuAsync`
(`src/SiNet.App.Wpf/Shell/NewShellFactory.cs`).

| Group | Menu label (Release) | Mode | Feature gate |
| --- | --- | --- | --- |
| פרויקטים ותבניות | **פתיחת פרויקט חדש** | Native create | `ProjectCreate` |
| פרויקטים ותבניות | **מיילים** | Gmail + ACC-filing (N1–N3) | `Shell.OpenEmailSurface` |
| פרויקטים ותבניות | **בעבודה 2** | Project files browse | `Shell.OpenProjectWorkSurface` |
| משימות | **לוח משימות** | Personal queues (read-only workbench) | `Shell.OpenTaskPanelReadOnly` |
| משימות | **דוחות ביקורת** | Native inspection reports | `Shell.OpenInspectionSurface` |
| משימות | **צפייה בתהליכים (סגור)** | Read-only workflow canvas | `Shell.OpenWorkflowClosedViewer` |
| דוחות | **R01 / R02 / R03** | MasterPlan → Google Sheets | `ReportsManagement` |
| משתמשים והרשאות | **ניהול / הוספת משתמש / הרשאות פעולה** | Native admin | `UsersManage` / `ActionPermissionsManage` |
| מנהלה | **הגדרות / מפתחות / מיפוי MasterPlan / סטטוס ACC / מצב מערכת / בריאות תהליכים** | Native admin / operator | Authenticated / `SystemSettingsWrite` / `Shell.OpenWorkflowOpsDashboard` |
| (host) | **NewShellWindow** | Menu + OS title (version via `NewShellWindowTitle`); **ProjectSelector is not in the shell header** -- Email embeds it | -- |

### Dev-only / harness (not production menu)

| Surface | Why | Entry |
| --- | --- | --- |
| **ביקורת (מעטפת — DEBUG)** | Developer harness | `#if DEBUG` + `Shell.OpenInspectionSurface` |
| **כלי פיתוח** | DEBUG admin tools | `#if DEBUG` |
| Legacy `EmailManagementView` / `MainWindow` | Full legacy email | V2 Legacy only |

---

## 4. Email — ACC-filing pilot

Title in UI: **"ניהול דואר — Gmail + ACC Inbox"**.

| Allowed | Blocked until policy / later slice |
| --- | --- |
| Gmail read (summaries, body, attachment metadata) | **GmailSend / Reply / Forward** (G-Policy) |
| Silent restore + explicit Connect | Broad outbound mail windows |
| ACC Inbox **ingest** (N1) | Unapproved ACC writes outside filing path |
| **Move to project** + Jumbo / external download (N2) | Workflow task completion from Email (unless via approved coordinator path) |
| ACC Inbox **recovery** (N3) | Full legacy EmailManagement parity |
| HTML viewer / open-after-upload where wired | — |

Sources of truth: Gmail label = mailbox filed; ACC = physical file; DB = helper cache.
See [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) and
[`EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md).

Deferred UI still hidden/disabled where retained (pagination/calendar/help placeholders, stub
LinkToProject / CreateTask / Archive / CompleteTask commands that are not on the N1–N3 path).
**Do not delete** suspended markup — mark inactive until a follow-up slice.

---

## 5. MasterPlan & Reports

| Slice | Status |
| --- | --- |
| S2 company/contact mapping | **Done** — מנהלה → **מיפוי MasterPlan** |
| S3 native R01 / R02 / R03 | **Done** — תפריט **דוחות** (`ReportsManagement`); User OAuth + Spreadsheets |
| S4 SyncEngine namespaces + logging | **Done** — `SiNet.Infrastructure.Logging` |
| Ops MasterPlan API key rotation | **Open** — [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) |
| Retire V2 R0x dual path | **After soak** — not this pilot gate |

UI parity vs V2 dialogs may be simplified (filters / R02 pivot); dual path retained until soak.

---

## 6. ACC host checklist (StandaloneNew)

| Check | Standalone |
| --- | --- |
| `AddSiNetAutodesk()` via `AddSiNet(StandaloneNew)` | Yes |
| Vault `ITokenProvider` | Yes (slice 2) |
| `AccService:BaseUrl` / mode (`IAccServiceModeProvider`) | Yes — default Remote `https://localhost:8443`; DB override |
| API key / vault diagnostics | Yes |
| Local inbox bootstrap (`AccBootstrapLocalInboxBootstrapExecutor`) | Yes when BaseUrl empty (slice 2b) |
| Remote `POST /v1/acc/inbox/ensure` | Yes when Remote |
| Email ACC N1–N3 ports / executors | Yes — see Email docs |
| Broad ACC write UI (provisioning screens, arbitrary metadata write) | **Blocked** until ACC-Write-Policy beyond approved filing |
| Prefer MultiStart AccService for pilot smoke | **Yes** |

---

## 7. Explicitly suspended (narrow)

| Area | Blocker | Status |
| --- | --- | --- |
| GmailSend / Reply / Forward in New System WPF | G-Policy | **Suspended** |
| Inspection Sheets create/export / screenshot Drive upload | Google / Inspection slice | **Deferred** |
| ACC write beyond approved Email filing / inbox ensure | ACC-Write-Policy | **Blocked** |
| Production switch of all legacy `GoogleService` consumers | Host cutover | **Blocked** |
| FloatingProjectTasks / WorkflowDashboard **write** in New Shell | Not migrated | **Deferred** |
| Workflow ops dashboard (read-only instances + stalled) | Native `WorkflowOpsDashboardWindow` | **Pilot** — see [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md); Retry/Cancel deferred |
| Broad task-aware window mutation | Integration contract | **Deferred** |
| V2 New System as pilot host | Standalone decision | **Out of envelope** |

---

## 7.1 Workflow production gate (standalone)

**Status: Conditional** — gate document and automated engine coverage are in place; **interactive soak**
(operator + `[WF-STEP]` log) is still required before claiming full workflow production readiness.

| Item | Detail |
| --- | --- |
| Master checklist | [`manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md) |
| Proposal detail runbook | [`manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md) |
| Trees in scope | PRP (email), OPN (email), PLN (+ hosted MAT), REV (+ MAT), integrity/watchdog, closed viewer |
| Release UI for progression | מיילים + לוח משימות + השלמת משימות via `ITaskCompletionService` — **not** WorkflowDashboard |
| DEBUG-only | Seed / Watchdog / `[WF-STEP]` (silence with `SINET_WF_DEBUG=0` after soak) |
| Known deferred | WorkflowDashboard write; `REV.Intake` seed not wired (see Review seed TODO) |

**Pass criteria (when soak completes):** PRP + OPN happy path + one critical branch each; PLN/REV either
Pass or an **approved Blocked** list; integrity Pass; closed viewer read-only OK; Release menu still
hides Dev Seed/Watchdog.

---

## 8. Verification (automated)

Full strategy: [`TEST_STRATEGY.md`](./TEST_STRATEGY.md) (L1–L4 offline + optional Live).

```powershell
dotnet build SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
dotnet test src\SiNet.Infrastructure.Google.Tests\SiNet.Infrastructure.Google.Tests.csproj
dotnet test src\SiNet.LegacyBridge.Tests\SiNet.LegacyBridge.Tests.csproj
```

Optional Live (local secrets / AccService):

```powershell
$env:SINET_LIVE_SMOKE = "1"
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --filter "Category=LiveSmoke"
```

Optional full CI-equivalent: `dotnet build SiNet.sln --configuration Release` then
`dotnet test SiNet.sln --configuration Release --no-build`.

**Do not** treat historical counts (e.g. **955/955** from 2026-07-05) as evidence for current HEAD —
always re-run tests on the branch under review.

Useful classes (non-exhaustive): `StandaloneHostCompositionTests`,
`NewShellReleaseMenuGatingTests`, `StandaloneStartupSequenceTests`,
`EmailAccSelectionHandlerStatusTests`, `ProductionPilotBoundaryTests`,
`StandaloneLocalAccInboxBootstrapTests`.

### 8.1 Automated run snapshot (2026-08-02) -- Historical Snapshot

> **Classification:** Historical Snapshot. Measured on branch `SiWorkNet10` on 2026-08-02 after the
> cutover / Workflow Ops runtime slice (offline + Live skipped). **Do not** treat these counts as
> evidence for current tip (`origin/release` / `development` @ `127dc0e`, App.Wpf **1.0.23**). Re-run
> tests on the branch under review.

Measured on branch `SiWorkNet10` after the cutover / Workflow Ops runtime slice (offline + Live skipped):

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `SiNet.App.Wpf.Tests` | **3028** | 0 | 6 (LiveSmoke, no `SINET_LIVE_SMOKE`) |
| **Build** `src/SiNet.App.Wpf` | ✅ | | |
| Sibling pins (`build/sibling-pins.json`) | Match local HEAD (no drift) | | |

Live layer (`Category=LiveSmoke`) was **Not Run** here (no AccService/DB session in the agent). Operators should run it locally before manual UI smoke.

---

## 9. Manual smoke (operator)

| Field | Value |
| --- | --- |
| **Interactive smoke** | **Not Run** |
| **P0 Live Smoke (L4W automated, 2026-08-24)** | **Pass** on DEV — SQL/Pilot corridor + Gmail/ACC; evidence `p0-pilot-smoke-20260824-154135.md` / `154602.md` — see [`PILOT_CONTROLS.md`](./PILOT_CONTROLS.md) § Live evidence; offline **3449** Pass |
| **Primary host** | `SiNet.App.Wpf.exe` + AccService MultiStart |
| **Operator checklist** | [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md) (+ workflow gate) |
| **Rollout** | [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) |
| **Pilot after Pass** | 1–2 internal users while external legacy system stays available |

### 9.1 Operator focus (standalone)

**Startup**

- [ ] MultiStart AccService then `SiNet.App.Wpf` (or equivalent Remote AccService).
- [ ] Vault / schema / Windows auth gates succeed.
- [ ] `NewShellWindow` opens; Legacy `MainWindow` does **not**.
- [ ] Gmail silent restore — no forced interactive login at startup.

**Shell / menu**

- [ ] Feature-gated: מיילים, בעבודה 2, לוח משימות, דוחות ביקורת, צפייה בתהליכים (סגור).
- [ ] When permitted: **דוחות** R01–R03, **מיפוי MasterPlan**, סטטוס ACC, admin surfaces.
- [ ] Release: no InspectionShell DEBUG harness / כלי פיתוח.

**Email ACC-filing**

- [ ] Title reflects Gmail + ACC Inbox (not “קריאה בלבד” only).
- [ ] Connect / refresh / search / details work.
- [ ] Ingest / Move / Jumbo-or-external / recovery per Email ACC smoke checklist when AccService up.
- [ ] **Absent:** Reply, Forward, Send as production actions.

**ACC status**

- [ ] Mode / health / diagnostics; no raw API key.
- [ ] Remote mode when BaseUrl set.

**Decision rules:** every required checklist section **Pass** (or N/A) → limited internal ACC-filing
pilot; any **Fail** → fix and re-run; missing credentials → blocked by environment.

### 9.2 Result template

```text
Date:
Operator:
Environment:
Branch:
Commit:
Host: SiNet.App.Wpf (+ AccService Remote Y/N)

Startup:            Pass / Fail / Blocked
Shell/menu:         Pass / Fail / Blocked
Email ACC-filing:   Pass / Fail / Blocked
MasterPlan/Reports: Pass / Fail / Blocked / N/A
ACC status:         Pass / Fail / Blocked
Admin/settings:     Pass / Fail / Blocked

Final decision:
  [ ] Ready for 1–2 internal ACC-filing pilot users
  [ ] Needs fix before pilot
  [ ] Blocked by environment/config
```

---

## 10. Open decisions & next slices

1. **Operator live smoke** (shell + Email ACC + optional Reports) — gate for pilot users.
2. **Workflow interactive soak** — [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md) (PRP→OPN→PLN→REV); flip §7.1 from Conditional to Pass/Fail.
3. **G-Policy** — whether native `GmailSend` / Reply / Forward may appear in New System WPF.
4. After smoke pass: **Email Composite Work Surface Contract** (docs only).
5. Ops: MasterPlan API key rotation (`OPS-P0-SECRET-ROTATION.md`).
6. Later: retire V2 R0x dual path; remove deprecated V2 New System startup after soak.

---

## 11. Related docs index

| Doc | Role |
| --- | --- |
| [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) | Host composition & slices 1–2b |
| [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md) | Layer / host-mode boundaries |
| [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) | Email ACC N1–N3 |
| [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) | Google scopes & G-Policy |
| [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md) | Mapping + Reports + SyncEngine |
| [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md) | AccService Local/Remote |
| [`APP_SHELL.md`](./APP_SHELL.md) | Shell / startup modes |
| [`TEST_STRATEGY.md`](./TEST_STRATEGY.md) | Automated + Live + manual layers |
| [`manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md) | Workflow trees + production gate |
| [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md) | DB recovery baseline |
