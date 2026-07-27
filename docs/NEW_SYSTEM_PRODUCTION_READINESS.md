# New System — Limited Production Pilot Envelope

> **Status:** Active (2026-07-27)  
> **Scope:** Defines what V2 New System mode may expose in a **limited production pilot**. This is
> **not** full legacy replacement, **not** broad window migration, and **not** a production switch
> away from `GoogleService` / legacy email management.
>
> Related:
> [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md),
> [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md),
> [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) (G-Startup closed; G-Policy pending),
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),
> [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md).

---

## 1. Pilot intent

V2 can launch **New System** (`RunNewSystemStartup` → `NewShellWindow`) as a **controlled production
pilot** when:

- Gmail silent restore runs at startup (G-Startup ✅).
- Only **read-only / operator** surfaces are menu-exposed.
- **No** stub or deferred action appears as an active production button.
- **No** harness window is exposed to regular users in release builds.
- Legacy production paths (`MainWindow`, `GoogleService`, `EmailManagementView`) remain unchanged.

---

## 2. Allowed in production pilot (now)

| Surface | Menu label (Release) | Mode | Feature gate |
| --- | --- | --- | --- |
| **EmailWindowView** | **מיילים** | Read-only Gmail | `Shell.OpenEmailSurface` |
| **ProjectWorkSurfaceHost** | **בעבודה 2** | Project files browse (in-memory host) | `Shell.OpenProjectWorkSurface` |
| **TaskPanelReadOnly** | **לוח משימות** | Personal Quick/Medium/Long queues (read-only workbench) | `Shell.OpenTaskPanelReadOnly` |
| **InspectionWindowView** | **דוחות ביקורת** | Native inspection reports surface | `Shell.OpenInspectionSurface` |
| **WorkflowClosedViewer** | **צפייה בתהליכים (סגור)** | Read-only workflow canvas (legend + templates; no save) | `Shell.OpenWorkflowClosedViewer` |
| **AccControlPlaneStatusWindow** | **סטטוס ACC** | Read-only / control-plane | `SystemSettingsWrite` (admin group) |
| **SecretSetupWindow** | **מפתחות וסודות** | Native admin — credential vault / keys | `SystemSettingsWrite` |
| **SettingsWindow** | **הגדרות אישיות / מערכת** | Native personal + system admin | Authenticated / `SystemSettingsWrite` |
| **UserListWindow / AddUserDialogWindow** | **ניהול משתמשים / הוספת משתמש** | Native admin | `UsersManage` |
| **ActionPermissionsWindow** | **הרשאות פעולה** | Native admin | `ActionPermissionsManage` |
| **NewShellWindow** | (host) | Project selector + menu only; not a workflow actor | — |

**EmailWindowView** — summaries, body/details, attachment **metadata** only. Deferred write/workflow actions
**hidden** and **disabled**.

**AccControlPlaneStatusWindow** — mode, health, diagnostics, browse, read-only reconciliation. No
upload/provisioning UI.

**Gmail read scope (allowed):**

- Silent restore via `IConnectorAuthService.TryRestoreSessionAsync` at V2 New System startup.
- Explicit connect (`Connect`) when session missing.
- Refresh / search / load details.

**ACC read scope (allowed):**

- Control-plane display, catalog/discovery browse, item lookup, read-only inbox reconciliation display.

---

## 3. Dev-only / harness-only (not production menu)

| Surface | Why | Dev entry point |
| --- | --- | --- |
| **InspectionShellView** | Developer harness; task-mode pilot incomplete | **DEBUG** shell menu only — **"ביקורת (מעטפת — DEBUG)"** |
| **InboxViewModel / standalone `SiNet.App.Wpf` MainWindow** | Scaffold harness | Not V2 production entry |
| **Legacy `EmailManagementView`** | Full production email (legacy) | Legacy `MainWindow` only — unchanged |
| **כלי פיתוח** (dev tools group) | DEBUG-only admin submenu | `#if DEBUG` in `BuildMigratedOnlyMenu()` |

**Note:** native **דוחות ביקורת** (`InspectionWindowView`) **is** in the Release menu when
`Shell.OpenInspectionSurface` is granted. Only the **InspectionShellView** harness remains DEBUG-only.

---

## 4. Deferred / blocked until policy slice

| Area | Blocker | Status |
| --- | --- | --- |
| GmailSend / Reply / Forward | G-Policy | **Suspended** — not wired in New System WPF |
| Gmail modify / labels | Policy | **Deferred** |
| Attachment open/download | Read slice gap | **Deferred** — metadata only in pilot |
| Email filing / MoveToProject / task completion from Email window | Workflow/filing slice + coordinator path | **Deferred** |
| Drive / Sheets / Reports | Legacy/deferred | **Do not expose** |
| ACC upload / provisioning / metadata write / folder ensure UI | ACC-Write-Policy | **Blocked** |
| Production `GoogleService` switch | Host switch decision | **Blocked** |
| FloatingProjectTasks / WorkflowDashboard in New Shell | Not migrated | **Deferred** |
| Broad task-aware window migration (beyond read-only TaskPanel + closed viewer) | Integration contract exists; write/mutation paths not pilot-ready | **Deferred** |

---

## 5. Production shell menu rules

Implemented in `NewShellFactory.BuildMigratedOnlyMenu()` (`src/SiNet.App.Wpf/Shell/NewShellFactory.cs`):

### 5.1 Release menu (feature-gated)

| Group | Menu label | Feature code | Host / factory |
| --- | --- | --- | --- |
| פרויקטים ותבניות | **מיילים** | `Shell.OpenEmailSurface` | `IEmailSurfaceHost` |
| פרויקטים ותבניות | **בעבודה 2** | `Shell.OpenProjectWorkSurface` | `ProjectWorkSurfaceHost` |
| משימות | **לוח משימות** | `Shell.OpenTaskPanelReadOnly` | `ITaskPanelReadOnlyWindowFactory` |
| משימות | **דוחות ביקורת** | `Shell.OpenInspectionSurface` | `IInspectionWindowFactory` |
| משימות | **צפייה בתהליכים (סגור)** | `Shell.OpenWorkflowClosedViewer` | `IWorkflowClosedViewerWindowFactory` |
| משימות | *(admin / settings / ACC — unchanged)* | per existing gates | native admin surfaces |

Additional groups (**משתמשים והרשאות**, **מנהלה**) unchanged — native admin, settings, ACC status, system
status; all gated by existing `AppFeatureCodes` + `IAuthorizationQueryService`.

### 5.2 DEBUG-only additions

| Menu item | When |
| --- | --- |
| **ביקורת (מעטפת — DEBUG)** | `#if DEBUG` + `Shell.OpenInspectionSurface` |
| **כלי פיתוח** subgroup | `#if DEBUG` dev-tools builder |

**No new feature-flag framework.** `#if DEBUG` guards harness/dev-tools only; Release surfaces use
existing `AppFeatureCodes` authorization.

### 5.3 Known startup gap (Stage 4 / HostMode)

`RunNewSystemStartup` in `SiNetProjectManagerV2/App.xaml.cs` may still open **legacy**
`WPF_Window.SecretSetupWindow` and `WPF_Window.ProvisioningPasswordDialog` during vault setup and DB
connection retry — **before** `NewShellWindow` appears. Native `SecretSetupWindow` is used from the
shell menu only. Full removal of legacy startup dialogs is deferred to **Stage 4 (HostMode)**.
See [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md) § Known startup gap.

---

## 6. EmailWindowView production guardrails

| Rule | Implementation |
| --- | --- |
| No stub buttons visible | `ShowDeferredWriteActions == false` hides write/workflow UI |
| Deferred commands disabled | `DeferredProductionPilotAction` → `CanExecute` always `false` |
| Code preserved | Commands remain for future slices — **not deleted** |
| Title / menu | "ניהול דואר — קריאה בלבד" |
| Visual placeholders hidden | `ShowDeferredVisualPlaceholders == false` hides pagination (`1 / 3`), calendar, help, date pickers |
| Clear search | `ClearSearchCommand` clears `SearchText` and reloads (real, minimal) |
| Production notice | `ProductionPilotNotice` in sidebar + viewer footer (replaces "שלד ויזואלי" copy) |
| Unread badge | Shown only when `ShowUnreadCount` (`UnreadEmailCount > 0`) |

Deferred actions (hidden + disabled): LinkToProject, CreateTaskFromEmail, MarkHandled, Archive,
Reply, Forward, OpenAttachment, CompleteTask.

**Still suspended (markup retained, hidden/disabled):** real pagination, Gmail date-range filtering,
calendar integration, help system. No new `IEmailGateway` query wiring in this polish slice.

---

## 7. Workflow / Task guardrails (production)

- ViewModels **must not** mutate `WorkflowStage` or `ProjectStatus` directly.
- Business task completion **must not** run from New System email/inspection surfaces in this pilot.
- Task open path (when added later): `TaskNavigationResolver` / `ITaskNavigationService` only — **no new router**.
- No first/last entity fallback when work target missing.

See [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md).

---

## 8. ACC host production checklist

ACC read/operator surfaces in New System **require V2 host** registration. Before expanding ACC beyond
status/operator read-only:

| Check | V2 host |
| --- | --- |
| `AddSiNetAutodesk()` in New System graph | ✅ `NewSystemServiceCollectionExtensions` |
| `AccService:BaseUrl` / mode via `IAccServiceModeProvider` | ✅ DB setting + config |
| API key / vault via `VaultAccServiceKeyDiagnostics` | ✅ Secret Setup |
| `ITokenProvider` for local mode | ✅ V2 `App.xaml.cs` host glue |
| `LegacyHostLocalAccInboxBootstrapExecutor` | ✅ host-only privileged local bootstrap |
| `IAccInboxReconciliationService` impl | Legacy `SiNetSQL` — port native, impl host-bound |
| Standalone `SiNet.App.Wpf` harness alone | ❌ incomplete ACC — not production representative |
| ACC write/upload/provisioning from New System WPF | ❌ **blocked** until ACC-Write-Policy |

**ACC-Host contract:** partially closed for read/operator; **not** closed for write/provisioning.

---

## 9. Verification (smoke)

```powershell
dotnet build SiNetProjectManagerV2/SiNetProjectManagerV2.csproj
dotnet test src/SiNet.App.Wpf.Tests/SiNet.App.Wpf.Tests.csproj
```

Key test classes:

- `ProductionPilotBoundaryTests`
- `GoogleFoundationClosureTests`
- `NewSystemBoundaryTests`
- `WorkSurfaceWorkflowIntegrationBoundaryTests`

### 9.2 Limited production smoke (2026-07-27)

| Field | Value |
| --- | --- |
| **Smoke status** | **Not Run** — pending operator completion of [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) |
| **Date** | 2026-07-27 |
| **Environment** | Local workspace `d:\repos2026\SiNetProjectManager_GitHub`; Debug + Release build |
| **User profile** | Agent/automated (no authenticated DB/Gmail session in smoke run) |
| **Git** | `git.exe` not on PATH; verified via VS bundled git + GitHub API — see §9.3 |
| **Build** | ✅ Debug + Release — 0 errors (263–274 pre-existing warnings, unrelated to pilot) |
| **Tests** | ✅ 955/955 `SiNet.App.Wpf.Tests`; ✅ 174/174 boundary filter |
| **Process launch** | ✅ V2 exe starts (brief run); full New System path not exercised to shell (requires mode/vault/DB UI) |

**Automated / static verification (passed):**

- `RunNewSystemStartup` → vault → DB → DI → `ServiceLocator.Initialize` → `StartNewSystemConnectorAuthRestore` → `LaunchNewSystemShell` (no Legacy fallback on failure).
- G-Startup: `TryRestoreSessionAsync` only — no `LoginAsync` in restore path.
- NewShell menu (Release): **מיילים**, **בעבודה 2**, **לוח משימות**, **דוחות ביקורת**, **צפייה בתהליכים (סגור)** — each feature-gated; InspectionShell harness `#if DEBUG` only; no Drive/Sheets/WorkflowDashboard write surfaces.
- Email pilot: `ShowDeferredWriteActions` / `ShowDeferredVisualPlaceholders` false; `ProductionPilotNotice`; `ClearSearchCommand`; no `OpenAttachmentCommand` in XAML.
- Release build succeeds (Inspection menu excluded from Release compilation).

**Manual smoke still required (operator checklist):**

Use [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) — supersedes the inline §9.3.1 list for Stage 2 P0 surfaces (DB, Vault, Gmail restore, ACC health/diag, Email, ProjectWork, Task Workbench, Inspection, Workflow closed viewer).

**Known issues:**

- Interactive runtime smoke **Not Run** (2026-07-27) — requires operator with DB/vault/Gmail/ACC session.
- `RunNewSystemStartup` may still show legacy SecretSetup/Provisioning dialogs before shell (Stage 4 HostMode fix) — see §5.3 and [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md).

**Decision:** **Needs manual interactive smoke** before limited users. After operator passes checklist in §9.3 → **ready for 1–2 internal read-only pilot users**.

### 9.3 Interactive smoke gate (2026-07-27)

| Field | Value |
| --- | --- |
| **Status** | **Not Run** — operator must complete [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) |
| **Date** | 2026-07-27 |
| **Required operator** | Human with V2 access, DB connection, credential vault, Gmail token/credentials, and ACC host/config |
| **Branch / commit** | `SiWorkNet10` (see latest commit on GitHub after push) |
| **Build / tests** | Debug + Release build ✅; `SiNet.App.Wpf.Tests` ✅ (see §9.2 automated run) |

**No automatic approval:** Agent/static tests and boundary guards **do not** authorize pilot users.
Only a completed manual checklist with **Pass** on every required step may change status to ready.

**Decision rules:**

| Outcome | When | Pilot decision |
| --- | --- | --- |
| **Ready for 1–2 internal read-only pilot users only** | Every checklist section **Pass** (or N/A where noted) | Approved for limited internal pilot |
| **Needs fix before pilot** | Any section **Fail** | Not ready — fix and re-run |
| **Blocked by environment/config** | Missing DB/vault/Gmail/ACC credentials or config | Not ready — resolve blockers first |

**GitHub / file audit (passed on `SiWorkNet10`):**

- `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` (this doc, including §9.3)
- `docs/UI_WINDOW_MIGRATION_MAP.md`
- `docs/NEW_SYSTEM_BOUNDARY.md`
- `docs/ACC_CONTROL_PLANE.md`
- `src/SiNet.App.Wpf/Shell/NewShellFactory.cs`
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs`
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml`
- `src/SiNet.App.Wpf.Tests/Boundary/ProductionPilotBoundaryTests.cs`

#### 9.3.1 Operator checklist (manual — required)

**Startup / mode**

- [ ] Launch V2 (`SiNetProjectManagerV2`).
- [ ] Startup mode selection appears (if configured).
- [ ] Select **New System**.
- [ ] `RunNewSystemStartup` path runs (vault → DB → DI → auth → shell).
- [ ] **NewShellWindow** opens.
- [ ] **Legacy MainWindow** does **not** open.
- [ ] No silent fallback to Legacy on failure.
- [ ] Credential vault setup works or shows a clear setup screen.
- [ ] DB connection works or shows a clear error (no silent continue).
- [ ] `StartNewSystemConnectorAuthRestore` runs silently — **no** interactive Google login at startup.

**Shell / menu**

- [ ] Menu shows **מיילים**, **בעבודה 2**, **לוח משימות**, **דוחות ביקורת**, **צפייה בתהליכים (סגור)** per role/feature gates.
- [ ] **InspectionShellView** harness (**ביקורת (מעטפת — DEBUG)**) **absent** in **Release** build.
- [ ] Inspection harness visible in **DEBUG** only (if feature gate allows).
- [ ] No **Reports**, **Drive**, **Sheets**, **WorkflowDashboard** write surfaces (unless explicitly approved elsewhere).
- [ ] **Secret Setup**, **Settings**, **Users**, **Permissions** appear only per `AppFeatureCodes` / role.
- [ ] No legacy admin windows open from NewShell.

**Email read-only**

- [ ] Open Email from NewShell.
- [ ] Title: **"ניהול דואר — קריאה בלבד"**.
- [ ] No visible **"שלד ויזואלי"** text.
- [ ] Production pilot notice visible.
- [ ] Project selector visible; select a real project → **ActiveProjectDisplay** updates.
- [ ] **Connect Google** works only when user clicks (not auto at startup).
- [ ] If valid token exists, session restores without extra login.
- [ ] **Refresh** loads Gmail summaries for project label.
- [ ] **Search** works; **ClearSearch** clears and reloads.
- [ ] Select email → body/details load.
- [ ] Attachments shown as **metadata only**.
- [ ] **Absent / disabled:** OpenAttachment, Reply, Forward, Send, MoveToProject, LinkToProject, CreateTask, MarkHandled, Archive, CompleteTask.
- [ ] Pagination / date / calendar / help placeholders not active.
- [ ] No Gmail modify / labels / mark-read from this window.

**ACC status**

- [ ] Open **AccControlPlaneStatusWindow** (סטטוס ACC).
- [ ] Mode displayed correctly.
- [ ] Health and diagnostics displayed.
- [ ] Key diagnostics show hash/prefix only — **no** raw secret.
- [ ] Read-only browse/lookup works if data exists.
- [ ] Read-only reconciliation works if sample exists.
- [ ] **Absent:** upload, provisioning, folder ensure as normal UI, metadata write, direct `Bim360Service` from WPF.

**Native admin / settings**

- [ ] **SecretSetupWindow** (native) opens.
- [ ] **SettingsWindow** (native) opens.
- [ ] **UserListWindow** / **AddUserDialogWindow** open per permission.
- [ ] **ActionPermissionsWindow** opens per permission.
- [ ] No legacy `UserManagementWindow`, `AddUserWindow`, `ActionPermissionWindow`, or legacy `SecretSetupWindow` from NewShell.

#### 9.3.2 Manual smoke result template

Copy into §9.3 (or a team log) after the operator run:

```text
Date:
Operator:
Environment:
Branch:
Commit:
DB status:        OK / Fail / Blocked
Vault status:     OK / Fail / Blocked
Gmail status:     OK / Fail / Blocked
ACC status:       OK / Fail / Blocked

Startup:          Pass / Fail / Blocked
Notes:

Shell/menu:       Pass / Fail / Blocked
Notes:

Email read-only:  Pass / Fail / Blocked
Notes:

ACC status:       Pass / Fail / Blocked
Notes:

Admin/settings:   Pass / Fail / Blocked
Notes:

Known issues:

Final decision:
  [ ] Ready for 1–2 internal read-only pilot users only
  [ ] Needs fix before pilot
  [ ] Blocked by environment/config
```

**After manual pass:** update §9.3 **Status** to **Passed**, fill template above, then open to 1–2 internal read-only users.

**Next documentation slice (after manual smoke pass):** **Email Composite Work Surface Contract** — docs only; defines component breakdown (project selector, search/filter, list, viewer, attachments/status, calendar/context, action panel, task completion) **before** any composite business logic.

### 9.1 SiWorkNet10 file checklist (audit)

These paths must exist on branch `SiWorkNet10` for the production pilot envelope to be considered
**closed on GitHub** (not only in a local workspace):

| Path | Purpose |
| --- | --- |
| `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` | This envelope |
| `src/SiNet.App.Wpf.Tests/Boundary/ProductionPilotBoundaryTests.cs` | Pilot guard tests |
| `src/SiNet.App.Wpf/Shell/NewShellFactory.cs` | Email read-only menu; Inspection `#if DEBUG` only |
| `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` | `ShowDeferredWriteActions`, disabled deferred commands |
| `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml` | Hidden write UI; read-only attachment chips |
| `docs/ACC_CONTROL_PLANE.md` | §5.1 Production host checklist |
| `docs/NEW_SYSTEM_BOUNDARY.md` | Cross-ref to this doc |

---

## 10. Recommended next production slice

**Email read-only production polish** — **closed** in this slice: visual placeholders hidden/disabled,
production-friendly notice copy, `ClearSearchCommand`, Hebrew empty state, unread badge when count > 0.
No write/send/workflow wiring.

**Option B (higher value, higher risk):** Email filing task-aware slice — only via existing
`MoveToProject` → handler → `ITaskCompletionCoordinator` path; no new ACC write from WPF.

**Do not** start with Option B until G-Policy + filing slice are explicitly approved.

**After polish:** run real smoke on V2 New System with limited users before expanding pilot audience.

**After manual smoke pass (§9.3):** publish **Email Composite Work Surface Contract** (docs only — no business logic).

---

## 11. Suspended / not deleted

| Item | Status |
| --- | --- |
| Full production replacement | **Suspended** |
| Broad legacy window migration | **Suspended** |
| InspectionShellView in production menu | **Suspended** (DEBUG/dev only) |
| Stub visual-clone actions | **Hidden/disabled**, code retained |
| Email visual placeholders (pagination, calendar, help, dates) | **Hidden/disabled**, markup retained — real integration deferred |
| GmailSend / Drive / Sheets / ACC write | **Suspended** |
| Legacy windows / GoogleService / old tasking model | **Retained** — not deleted |
