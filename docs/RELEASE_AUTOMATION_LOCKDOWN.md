# Release Automation Lockdown

> **Title:** Release must not contain or execute test / DEV automation  
> **Date:** 06.09.2026  
> **Status:** Active  
> **Scope:** Production desktop host `SiNet.App.Wpf` (Release / MSIX). Test projects may remain in Git.  
> **Verdict required before pilot install:** `RELEASE TEST/DEV AUTOMATION = DISABLED`

Related: [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md), [`PILOT_CONTROLS.md`](./PILOT_CONTROLS.md), [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`TEST_STRATEGY.md`](./TEST_STRATEGY.md), [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md).

---

## 1. Release rule (locked)

In a **Release** build of `SiNet.App.Wpf`:

| Forbidden | Allowed (product) |
| --- | --- |
| PilotSmoke / LiveSmoke / SystemCert runners | Auth / session restore (Gmail silent restore, Windows SIUser) |
| E2E / workflow seed execution from host | Health / System Status refresh (read) |
| Certification or WpfPilot automation runner | Gmail History polling → **list reload only** |
| DEBUG Inspection harness DI + menu | ProjectWork disk reconcile / file-index refresh |
| DevTools Reset / Seed / Demo Tasks UI | Task Workbench cross-client **reload** |
| Env vars that trigger test mutation (`SINET_PILOT_SMOKE*`, `SINET_SYSTEM_CERT*`, `SINET_LIVE_SMOKE`) from product code | Normal infrastructure init (vault, schema gate, logging) |
| Automatic workflow start / task completion / ACC filing / Gmail label mutate / outbound send caused by **test infrastructure** | Explicit user / supported product workflow actions |

User actions and intended product background services are **not** disabled by this lockdown.

---

## 2. Runtime graph matrix (standalone Release)

| Component | Dev available? | Release compiled? | Release registered? | Release reachable? | Can mutate data? | Action |
| --- | --- | --- | --- | --- | --- | --- |
| `InspectionShellViewModel` + tree/notes/drawings harness | Yes | Type source remains | **No** (`#if DEBUG`) | No | Would (harness UI) | Compiled-out of DI |
| `InspectionShellView` / OpenInspectionShell menu | Yes | Method source remains | Menu `#if DEBUG` | No | N/A | Keep menu `#if DEBUG` |
| `AddSiNetDevTools` mutating seed/reset/demo | Yes | Stub types only | Stubs + verify | Stubs throw / verify read-only | **No** (stubs) | Mutating impl `#if DEBUG` only |
| `SqlWorkflowSeedService` / `SqlTaskDemoSeedService` | Yes | Source in Git | **No** in Release DI | No | Yes if called | Not registered in Release |
| DevTools menu «כלי פיתוח» | Yes | Coordinator source remains | Menu `#if DEBUG` | No | Yes if opened | Already `#if DEBUG` |
| Debug Authorization Role Selector | Yes | **No** (`#if DEBUG`) | No | No | Yes (SIUser role) | Already `#if DEBUG` |
| PilotSmoke / SystemCert / LiveSmoke | Tests only | Not in App.Wpf | No | No | Yes in tests | Stay in `SiNet.App.Wpf.Tests` |
| WpfPilot / FlaUI | External / tests | No runner in host | No | No | N/A | AutomationId metadata only |
| `ISeedBaselineVerifyService` | Yes | Yes | Yes | System Status | **No** (read) | Retain |
| Gmail History poll (3 min) | Yes | Yes | Yes | Email surface open | **No** (reload list / checkpoint) | Retain |
| Task Workbench poll | Yes | Yes | Yes | Workbench open | **No** (reload) | Retain |
| ProjectWork reconcile timer | Yes | Yes | Yes | ProjectWork open | Local index/UI sync (not ACC filing) | Retain |
| System Status PeriodicTimer | Yes | Yes | Yes | Shell | **No** (probes) | Retain |
| AccService / Gmail product actions | Yes | Yes | Yes | User / workflow | Yes when user/workflow triggers | Retain |

---

## 3. Compile-out / fail-closed (code)

| Change | Where |
| --- | --- |
| Inspection harness `TryAddSingleton` block | `StandaloneHostServiceCollectionExtensions` `#if DEBUG` |
| Release DI descriptor scan | `ReleaseDevAutomationGuard.AssertStandaloneHostDescriptorsClean` (`#if !DEBUG`) |
| Mutating DevTools registrations | `DevToolsServiceCollectionExtensions` — seed/reset/demo/`SqlWorkflowSeed*` only `#if DEBUG` |
| DevTools menu | `NewShellFactory` — already `#if DEBUG` |
| Role selector | `App.xaml.cs` — already `#if DEBUG` |

---

## 4. Test projects may remain in Git

- `SiNet.App.Wpf.csproj` must **not** `ProjectReference` `SiNet.App.Wpf.Tests`.
- MSIX payload must not contain `SiNet.App.Wpf.Tests.dll`, `xunit*.dll`, testhost, PilotSmoke scripts.
- `InternalsVisibleTo` for tests is allowed (compile metadata only).
- AutomationId / AutomationProperties.Name on WPF controls are **metadata only** — keep them.

---

## 5. Automatic mutation classification (WPF runtime)

| Path | Class | Trigger |
| --- | --- | --- |
| Vault + schema gate + authorize | INITIALIZATION | Startup |
| Gmail silent restore | INTENDED PRODUCT | Startup / connect |
| Gmail History poll → RefreshPage | READ-ONLY / INITIALIZATION | Email window open; 3 min timer |
| History checkpoint commit | INTENDED PRODUCT (cursor) | After successful reload |
| System Status contributors | READ-ONLY | Timer / Refresh |
| Task Workbench LoadAsync poll | READ-ONLY | Workbench open |
| ProjectWork reconcile / watcher | INTENDED PRODUCT (local tree) | ProjectWork open |
| MoveToProject / label / Start / CompleteTask | DATA MUTATION | Explicit user / workflow command |
| Dev seed / reset / demo / Inspection harness | TEST/DEV ONLY | DEBUG only — must be unreachable in Release |

---

## 6. Release menu audit (required)

Release shell must **not** expose user-facing items whose title/role is DEBUG / DEV / Harness / Seed / Smoke / Certification / Automation runner / «כלי פיתוח».

Production Inspection surface remains **«דוחות ביקורת»** (real `IInspectionWindowFactory`), not the DEBUG InspectionShell harness.

---

## 7. MSIX payload audit (required after package)

Confirm **absent** from published payload:

- `SiNet.App.Wpf.Tests.dll`
- `xunit*.dll` / `testhost*.dll` / `Microsoft.NET.Test.Sdk*`
- PilotSmoke / certification runner binaries or scripts
- DEV harness executable

AutomationId **strings** inside `SiNet.App.Wpf.dll` are allowed.

**2026-09-06 local package audit** (`publish-desktop.ps1 -SkipDeploy -NoBump`, payload under `artifacts/SiNet.App.Wpf_Package/payload`):

| Check | Result |
| --- | --- |
| Forbidden test/xunit/testhost/PilotSmoke file names | **NONE** |
| `OpenInspectionShell` / `BuildDevToolsMenuItemsAsync` in Wpf.dll | **absent** |
| `DevToolsCoordinator` type metadata | **absent** (DEBUG compile-out) |
| `PilotSmoke` in Wpf.dll | **absent** |
| Hebrew DevTools menu titles in Wpf.dll | **absent** |
| `ReleaseDevAutomationGuard` present | yes (fail-closed) |
| Inspection harness **types** still in Wpf.dll (unregistered) | yes — DI `#if DEBUG` only |
| Mutating seed **services** still in Sql.dll (unregistered) | yes — Release registers stubs only |

---

## 8. Out of Scope

- Deleting test projects from Git
- Disabling Gmail History, System Status, or ProjectWork reconcile
- Changing Pilot.Enabled SystemSettings policy (ops)
- AccService / SyncEngine process hosts (separate channels)

## 9. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Hide Inspection harness menu only (leave DI) | **Dropped** | DI remained reachable; now `#if DEBUG` |
| Delete WpfPilot AutomationIds | **Dropped** | Metadata only; accessibility |

## 10. Change log

| Date | Change |
| --- | --- |
| 06.09.2026 | Initial lockdown: compile-out Inspection harness DI; Release DevTools mutating services; `ReleaseDevAutomationGuard`; boundary tests. |
