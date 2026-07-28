# New System — Limited Production Pilot Envelope

> **Status:** Active (2026-07-28) — rewritten for **standalone** host  
> **Scope:** Defines what **`SiNet.App.Wpf.exe`** may expose in a **limited production pilot**.
> This is **not** full legacy replacement, **not** broad window migration, and **not** approval
> of GmailSend / Reply / Forward (G-Policy still open).
>
> Locked host decision: New System = `SiNet.App.Wpf.exe` only; Legacy = `SiNetProjectManagerV2.exe`
> only. See [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md).
>
> Related:
> [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md),
> [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md),
> [`manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) (G-Policy = Send/Reply/Forward only),
> [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md),
> [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md),
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md).

---

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

---

## 2. Host model

| Process | Role |
| --- | --- |
| **`SiNet.App.Wpf.exe`** | **Production New System** — this pilot envelope |
| **`SiNetProjectManagerV2.exe`** | **Legacy** mode only |
| V2 “New System” startup | **Deprecated + logged** — not part of this pilot envelope, checklist, or smoke gate |

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
| מנהלה | **הגדרות / מפתחות / מיפוי MasterPlan / סטטוס ACC / מצב מערכת** | Native admin / operator | Authenticated / `SystemSettingsWrite` |
| (host) | **NewShellWindow** | Project selector + menu | — |

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
| FloatingProjectTasks / WorkflowDashboard write in New Shell | Not migrated | **Deferred** |
| Broad task-aware window mutation | Integration contract | **Deferred** |
| V2 New System as pilot host | Standalone decision | **Out of envelope** |

---

## 8. Verification (automated)

```powershell
dotnet build SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
```

Optional full CI-equivalent: `dotnet build SiNet.sln --configuration Release` then
`dotnet test SiNet.sln --configuration Release --no-build`.

**Do not** treat historical counts (e.g. **955/955** from 2026-07-05, or any older §9.2.1 table) as
evidence for current HEAD — always re-run tests on the branch under review.

Useful boundary classes (non-exhaustive): `ProductionPilotBoundaryTests`,
`StandaloneNewSystemHostBoundaryTests`, `StandaloneLocalAccInboxBootstrapTests`,
`NewSystemBoundaryTests`, `GoogleFoundationClosureTests`.

---

## 9. Manual smoke (operator)

| Field | Value |
| --- | --- |
| **Interactive smoke** | **Not Run** |
| **Primary host** | `SiNet.App.Wpf.exe` + AccService MultiStart |
| **Shell / Stage-2 surfaces** | [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) — interpret launch steps as **standalone App.Wpf**, not V2 New System |
| **Email ACC N1–N3** | [`manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md) |
| **Pilot after Pass** | 1–2 internal **ACC-filing** pilot users (not send/reply) |

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
2. **G-Policy** — whether native `GmailSend` / Reply / Forward may appear in New System WPF.
3. After smoke pass: **Email Composite Work Surface Contract** (docs only).
4. Ops: MasterPlan API key rotation (`OPS-P0-SECRET-ROTATION.md`).
5. Later: retire V2 R0x dual path; remove deprecated V2 New System startup after soak.

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
| [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md) | DB recovery baseline |
