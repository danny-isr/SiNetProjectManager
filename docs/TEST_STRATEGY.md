# Standalone New System — Test Strategy

> **Status:** Active (2026-07-29)  
> **Host:** `SiNet.App.Wpf.exe` (`SiNetHostMode.StandaloneNew`)  
> **Pilot envelope:** [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md)  
> **Manual checklist (operator-only):** [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md)

This document defines the **test layers** for the limited standalone pilot: what runs in CI,
what can be run locally with secrets, and what always stays manual.

---

## 1. Layers

| Layer | Where | Runs in CI | Needs secrets / network |
| --- | --- | --- | --- |
| **L1** Unit / ViewModel + stubs | `src/SiNet.App.Wpf.Tests`, Google.Tests, LegacyBridge.Tests | Yes | No |
| **L2** Boundary / source guards | `src/SiNet.App.Wpf.Tests/Boundary`, `Shell`, docs asserts | Yes | No |
| **L3** Composition smoke (offline) | `Composition/StandaloneHostCompositionTests`, menu gating, startup guards | Yes | No |
| **L4** Live smoke (env-gated, **read-only**) | `src/SiNet.App.Wpf.Tests/Live` | Skipped (no env) | Yes |
| **L4W** P0 Pilot smoke (env-gated, **writes**) | `src/SiNet.App.Wpf.Tests/Live` (`Category=PilotSmoke`) | Skipped (no env) | Yes + DEV DB + operator confirmation |
| **L5** Manual operator checklist | `manual-tests/STANDALONE_PILOT_SMOKE.md` | No | Yes + human UI |

```text
L1 Unit/VM → L2 Boundary → L3 Composition (CI) → L4 Live read (optional) → L4W Pilot write (DEV only) → L5 Manual
```

**L4 vs L4W:** L4 only observes (connect, health, silent token restore). L4W creates projects, workflow instances, tasks, Gmail labels and ACC folders/items. They are separate categories so `Category=LiveSmoke` can never trigger writes.

---

## 2. CI gate (official)

Workflow: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) on `SiNet.sln`:

```powershell
pwsh ./build/fetch-siblings.ps1
dotnet restore SiNet.sln
dotnet build SiNet.sln --configuration Debug --no-restore
dotnet build SiNet.sln --configuration Release --no-restore
dotnet test src/SiNet.App.Wpf.Tests/SiNet.App.Wpf.Tests.csproj --configuration Release --no-build
dotnet test src/SiNet.Infrastructure.Google.Tests/SiNet.Infrastructure.Google.Tests.csproj --configuration Release --no-build
dotnet test src/SiNet.LegacyBridge.Tests/SiNet.LegacyBridge.Tests.csproj --configuration Release --no-build
pwsh ./build/secret-scan.ps1
```

Local agent gate (host + primary tests):

```powershell
dotnet build SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
```

Live tests (`Category=LiveSmoke`) are **skipped** unless `SINET_LIVE_SMOKE=1` — they do not fail CI.

---

## 3. Offline automation that replaces checklist steps

| Manual concern | Automated pre-check |
| --- | --- |
| DI loads / ports resolve | `StandaloneHostCompositionTests` |
| Menu visible per feature gate | `NewShellReleaseMenuGatingTests` |
| DEBUG harness not in Release source path | same + `#if DEBUG` source guards |
| Startup order: vault → schema → auth → shell | `StandaloneStartupSequenceTests` |
| Email ACC button/status text states | `EmailAccSelectionHandlerStatusTests` |
| No duplicate DI registrations (StandaloneNew) | composition tests |

---

## 4. Live smoke (optional, local)

Requires a developer machine with real vault/DB/AccService (and optionally a restored Gmail token).

```powershell
$env:SINET_LIVE_SMOKE = "1"
# Optional overrides:
# $env:SINET_LIVE_SQL_CONNECTION = "Server=...;Database=...;..."
# $env:SINET_LIVE_ACC_BASEURL = "https://localhost:8443"

dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --filter "Category=LiveSmoke"
```

| Test class | What it checks |
| --- | --- |
| `SqlConnectivityLiveTests` | SQL connect + `IDatabaseSchemaGate` present |
| `VaultLiveTests` | AccService API key metadata readable; raw secret not logged in diagnostics |
| `AccServiceHealthLiveTests` | `GET /v1/acc/health` (no key) + `GET /v1/acc/diag` (with key) |
| `AccModeLiveTests` | Mode is Remote when BaseUrl configured |
| `GmailSilentRestoreLiveTests` | `TryRestoreSessionAsync` only — never opens a browser; Skip if no token |

**Environment defaults:** AccService `https://localhost:8443`; SQL from `SINET_LIVE_SQL_CONNECTION` or vault key `SiNet/ConnectionStrings/SiNetDatabase`.

---

## 4W. P0 Pilot smoke — the write tier (DEV only)

Automates the corridor that [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md) previously required an operator to walk by hand. It proves the P1 Pilot controls in [`PILOT_CONTROLS.md`](./PILOT_CONTROLS.md) against a real database, a real Gmail mailbox and disposable ACC projects.

**This tier writes.** It is fail-closed: every gate below must be present or the test **skips**, so CI and the read-only L4 tier stay unaffected.

### 4W.1 Gates

| Tier | Variable | Purpose |
| --- | --- | --- |
| SQL | `SINET_LIVE_SMOKE=1` | existing live tier gate |
| SQL | `SINET_PILOT_SMOKE=1` | explicit write opt-in |
| SQL | `SINET_PILOT_SMOKE_SQL` | connection string, **no vault fallback ever** |
| SQL | `SINET_PILOT_SMOKE_DB_CONFIRM` | must match the `Database` parsed from the connection (double entry) |
| SQL | `SINET_PILOT_SMOKE_USER_ID` | operator `SIUser.Id` acting as the pilot user |
| Gmail | `SINET_PILOT_SMOKE_GMAIL=1` | Gmail opt-in |
| Gmail | `SINET_PILOT_SMOKE_GMAIL_SUBJECT` | subject token identifying the operator's test message |
| Gmail | `SINET_PILOT_SMOKE_GMAIL_ACCOUNT` | expected authenticated mailbox; abort on mismatch |
| ACC | `SINET_PILOT_SMOKE_ACC=1` | ACC opt-in (requires the Gmail tier) |
| ACC | `SINET_PILOT_SMOKE_ACC_INBOX_PROJECT` | disposable ACC project name written temporarily into `InboxProjectName` |
| ACC | `SINET_PILOT_SMOKE_ACC_PLACE` | must be `SI` |

Deliberately **not** supported: resolving the connection string from the vault. `LiveEnvironment.TryResolveSqlConnectionString()` does fall back to `SiNet/ConnectionStrings/SiNetDatabase`, which on a PROD machine is production — a write tier must never inherit that behaviour.

### 4W.2 Why ACC needs its own guard

Two independent ACC targets exist, and **only one follows the project's Place**:

| Target | Resolved from | Follows Place? |
| --- | --- | --- |
| Per-project filing / MoveToProject | `"SI-" + Place.Title` (`AccProjectProvisioningService.AccProjectPrefix`) | Yes |
| Office Inbox ingest | `InboxProjectName` system setting, fallback `"מיילים למשרד - POC 4"` | **No** |

Because a DEV database restored from a production backup carries the production `InboxProjectName`, an unmodified ingest would upload into the production Inbox project. There is no `#if DEBUG` redirection anywhere in the ACC path, and the `SI` place rule in [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) §5.1 is still a naming convention rather than a code gate.

`PilotSmokeAccGuard` therefore adds, **in the harness only**:

1. Pre-flight that the effective `InboxProjectName` equals the declared disposable name and the smoke project's Place title is exactly `SI` (see §4W.2.2 — that row is ensured, not assumed).
2. Decorators over `IAccFileUploadService` and `IAccItemMetadataService` that reject any ACC project id outside an allowlist containing only ids this run created or verified. The allowlist starts **empty**; ids are added only after the run has created or verified that specific disposable project.
3. Never resolving `IProjectCreateService` from the container, because `SqlProjectCreateService.CreateAsync` provisions ACC eagerly after commit with no feature flag. The harness constructs `SqlProjectCreateService` with only the `DbContext` factory, leaving both the ACC provisioner and the folder bootstrapper null.

### 4W.2.1 The Office Inbox override is only honoured in Local mode

Found while implementing the tier, and it defeats defence 1 on its own:

- `ModeSwitchingAccInboxBootstrapService` picks Local or Remote from `IAccServiceModeProvider`.
- The Local executor (`AccBootstrapLocalInboxBootstrapExecutor`) resolves `InboxProjectName` in-process through `ISystemSettingsQueryService`, so it sees the smoke override.
- The Remote path POSTs to AccService `/acc/inbox/ensure`, and **AccService resolves `InboxProjectName` from its own database**. An override written into the smoke database is simply ignored, and ingest would target whatever the AccService database names — on a workstation configured against production, the production Inbox project.

The harness therefore binds `IAccInboxBootstrapService` to a local-only wrapper and **throws** if the local executor is unavailable, rather than falling back to Remote. Uploads themselves are unaffected: they carry an explicit project id from this process, which is exactly what the allowlist decorator checks.

The same asymmetry deserves a look outside the test tier (**Needs Review**): any DEV operation that changes an ACC-relevant system setting locally has no effect on a Remote AccService reading a different database.

### 4W.2.2 The `SI` place does not exist in a restored database

Measured on the DEV workstation, 2026-08-24: the database carries 1,284 `Place` rows, all Hebrew locality names, and **none titled `SI`**. The only existing mapping is `ProjectId=3147 → SI-אביגדור`, which confirms the `"SI-" + Place.Title` derivation but also that §5.1's `SI` convention has no row behind it.

Every other precondition in this tier reports **Blocked** rather than seeding. This one is the exception: the write tier **ensures** a `Place` titled `SI` exists, idempotently, because

- it is the guard's own precondition — without it the tier cannot run at all, and
- the DEV database is restored from backup regularly, so a one-off manual insert would disappear on the next restore.

It is a single lookup row, it is recorded in the evidence file, and it is listed for cleanup. The tier still refuses to seed anything behavioural: workflow definitions, project-type mappings and seed baseline gaps are all reported as Blocked.

### 4W.2.3 The operator's `LoginName` comes from the production server

Same cause, second symptom. A restored database carries the **production** server's `SIUser.LoginName` values (`SI-ENG\…`), while the DEV workstation authenticates as a different identity (measured 2026-08-24: `AzureAD\dannyisrael`). Until one row is repointed, the Windows identity resolves to nobody and the host cannot authenticate at all.

The tier therefore ensures the declared operator row is the one the current Windows identity resolves to, and nothing more:

- It **never creates** a user, and never changes `Role`, `IsActive`, `Email`, `Name` or group memberships — those decide who workflow tasks are assigned to, and inventing them would make the corridor proof meaningless.
- If the Windows identity already resolves to a **different** active user, it **refuses** instead of moving a login between users.
- If the declared operator row is missing or inactive, it reports Blocked; activating a user is a permission decision.

The change is persistent by design and is recorded in the evidence file with the previous value.

### 4W.3 Scope

| Step | Automated | Notes |
| --- | --- | --- |
| Fail-closed root Start rejection | Yes | no instance, no task, no partial mutation |
| Narrow enable, allowlisted Start | Yes | `Pilot.AllowedWorkflowCodes=Proposal` only |
| Non-allowlisted user / workflow code | Yes | both rejected |
| PRP corridor via email action + `ITaskCompletionService` | Yes | never Dashboard mutation, never Ops Advance |
| `QuoteApprovedByClient` blocked by PLN | Yes | the critical P1 safety proof |
| Kill-switch semantics | Yes | new Start blocked, existing task still advances |
| Gmail label filing round-trip | Yes | reversible via `UnfileFromProjectAsync` |
| ACC Inbox ingest + MoveToProject | Yes | disposable projects; operator deletes them afterwards |
| WPF visual / dismiss / `CompleteAsync` | **No** | `EmailDetailViewModel` owns these — stays L5 |

### 4W.4 Restore and cleanup

Restored in `finally`: `Pilot.Enabled`, `Pilot.AllowedUserIds`, `Pilot.AllowedWorkflowCodes`, `InboxProjectName`, the single `AccSystemResource` row keyed `OfficeInbox`, and the Gmail project label on the test message.

Not removable by the harness, reported for manual deletion: uploaded ACC items and versions (the application only soft-deletes via `HideAsync`), the disposable ACC inbox project, and `SI-SI` **only if this run created it**.

### 4W.5 Running it

```powershell
pwsh .\build\run-p0-pilot-smoke.ps1 -Probe   # prints targets only, writes nothing
pwsh .\build\run-p0-pilot-smoke.ps1          # runs Category=PilotSmoke, prints evidence path
```

The authoritative guard lives in code, not in the script.

---

## 5. Always manual (never automated here)

- MultiStart launch of `SiNet.App.Wpf.exe` + AccService process
- Interactive OAuth consent / first-time Google login
- WebView2 Jumbo / WeTransfer download UX
- ACC **recovery** flows, and any ACC operation against a **production** project
- MasterPlan R01–R03 write to real Google Sheets
- Visual navigation polish (project selector, layout, Hebrew copy)
- `EmailDetailViewModel` dismiss / `CompleteAsync` UI ownership per [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md)

ACC Inbox upload and MoveToProject are no longer purely manual: §4W automates them against **disposable** ACC projects only. Against production projects they remain manual.

Those remain in [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md).

---

## 6. Related docs

| Doc | Role |
| --- | --- |
| [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) | Pilot envelope + verification pointer |
| [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) | Host composition |
| [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) | Email ACC N1–N3 |
| [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md) | AccService Local/Remote |
| Superseded: [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) | Replaced by STANDALONE_PILOT_SMOKE |
| Superseded: [`manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md) | Folded into STANDALONE_PILOT_SMOKE § Email ACC |
