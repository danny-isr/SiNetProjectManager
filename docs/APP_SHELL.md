# SiNet Application Shell (`SiNet.App.Wpf` / NewShellWindow)

> **Title:** Application shell (production host = `SiNet.App.Wpf`)
> **Date:** 02.08.2026
> **Updated:** 07.08.2026 (As-Is reconciliation -- production path, ProjectSelector placement, branch SoT)
> **Status:** Active / Current Source of Truth (shell As-Is + remaining isolation goals)
> **Scope:** Production desktop shell (`NewShellWindow`), menu/factory, project context wiring, vs deprecated V2 dual-mode host.
> **Working branches:** `release` (ship) + `development` (DEV) -- see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3. `SiWorkNet10` is deprecated (not the working SoT).
> **New solution:** `SiNet.sln` · **Legacy/functional reference:** `SiNetProjectManager.sln` / `SiNetProjectManagerV2` (code reference; not the publish channel)
> **Reconciliation ledger:** [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md)

This document describes the **application shell** and how the production desktop host starts.
It refines [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) and
[`MIGRATION_MAP.md`](./MIGRATION_MAP.md). When code and this document disagree, fix the document first,
then the code (see [`README`](./README.md) documentation-driven workflow).

> Related: [`PROJECTS.md`](./PROJECTS.md) (Project Context),
> [`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md) (Historical Snapshot),
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) (pilot envelope),
> [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) (standalone composition).

### Cutover decision (2026-08-02) -- As-Is

| Topic | Decision / As-Is |
| --- | --- |
| Production desktop app | **`SiNet.App.Wpf.exe` only** (MSIX channel 3 in `publish-all.ps1`) |
| `SiNetProjectManagerV2` | Remains in the repo and in hybrid builds as a **code reference**; **not** distributed via `publish-all.ps1` |
| Coexistence on user machines | **Not required** -- V2 was never the office production install; safety net until cutover sign-off is an **external** legacy system |
| Feature readiness bar | Daily work must be possible; not full parity with every V2 screen |
| Workflow definitions | Closed-world seed (six codes; no visual editor in New System) -- see [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md) |

---

## 1. Purpose of the application shell

The **application shell** is the top-level window (`NewShellWindow`) that hosts migrated New System
surfaces without opening the legacy V2 `MainWindow`. It exists to:

- Provide the **production entry point** for the migrated stack (`SiNet.App.Wpf`).
- Keep startup and menu graph isolated from the legacy host (measurable without loading V2 menus).
- Give migrated Work Surfaces (Email, Inspection, Project Context, Settings, admin) a home as they
  are ported, following the *Work Surfaces* model in `ARCHITECTURE_TARGET.md` §4.

**Production path (As-Is):** users run **`SiNet.App.Wpf`** (standalone host). `SiNetProjectManagerV2`
is kept for reference/build only and is **not** the distribution channel.

---

## 2. Production host vs V2 reference (Legacy mode is historical)

```plaintext
Production (As-Is)  → SiNet.App.Wpf.exe (standalone)
                    → NewShellWindow + migrated menu only
                    → does NOT open V2 MainWindow
                    → surfaces load on demand via DI

V2 reference / hybrid (Historical / Not published)
                    → SiNetProjectManagerV2 may still offer StartupModeSelectionWindow
                      (New System vs Legacy) for developers
                    → Legacy mode opens V2 MainWindow -- NOT office production
                    → New System mode inside V2 is deprecated (logs deprecation; pilot fallback only)
```

**As-Is framing:** do **not** describe Legacy mode as "current production." Office production is
**`SiNet.App.Wpf` only**. Dual-mode (New System vs Legacy) was a migration aid inside V2; it is
**not** the shipped dual-product model.

Where V2 still shows **`StartupModeSelectionWindow`** (developer / hybrid builds):

- **One prompt only** -- `ShowSplashThenMainWindow()` does **not** ask for mode again (Legacy: splash →
  `MainWindow` only).
- **New System** builds `ServiceProvider` via `ConfigureServices()` **before** opening `NewShellWindow`
  (DI required for `INewShellFactory`, Project Context, Email/Inspection factories). It **skips**
  legacy DB retry loop, schema validation, debug role selector, and user authorization gates.
- **No silent Legacy fallback** -- if New System shell creation fails, show an error and **shutdown**;
  do not open `MainWindow` in the background.

> **Non-goal / anti-pattern (explicit):** New system mode must **not** be implemented by opening the
> legacy `MainWindow` and hiding menu items. That would still load the legacy system and defeat the
> performance-isolation goal.

---

## 3. Startup flow

**Production entry (As-Is):** `SiNet.App.Wpf.exe` -- see
[`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md). Standalone host runs vault /
schema / Windows-auth gates, then opens `NewShellWindow`. The V2 New System path is
**deprecated** (still runnable for developer/hybrid fallback; logs a deprecation warning).

**Branding (standalone):** User-facing brand is Hebrew **שיא חדש בע״מ** (not English "SiNet" /
"SI"). Assets: `logo_si.jpg` (full company logo), `shia-chadash-mark.png` (road-in-circle mark),
`sinet.ico` (ApplicationIcon built from that mark). `StartupSplashWindow` shows the company logo
during vault/schema/auth; `NewShellWindow` header uses the road circle + "שיא חדש בע״מ".
`StartupModeSelectionWindow` (V2 host only) uses the same mark after
`ThemeResourceLoader.EnsureApplicationResourcesMerged()`.

Startup is **code-driven** (no XAML `StartupUri`) in each host's `App.xaml.cs`.

```plaintext
Production -- SiNet.App.Wpf App.OnStartup
  → vault / schema / Windows auth (standalone gates)
  → AddSiNetStandaloneHost / NewShellWindow
  → no V2 MainWindow; no StartupModeSelectionWindow

V2 hybrid (Historical / Not published) -- App.OnStartup
  → StartupModeSelectionWindow (FIRST visible UI; default = New System)
        ├─ cancel / close → Shutdown
        ├─ New System   → ConfigureServices → NewShellWindow (deprecated path)
        └─ Legacy       → credential vault → DB gate → schema → auth → splash → MainWindow
```

On the V2 hybrid path, the mode prompt is shown **before any legacy gate** so New system mode can
skip credential/DB/schema dialogs. The choice is captured as a `StartupMode` value and routed by
`StartupModeRouter` (unit-testable without WPF).

> Current-user selection: in **both modes**, startup authorizes the current **Windows identity**
> against `SIUser` (`AuthorizeCurrentUser`) after optional DEBUG role-selector when enabled.
> **New system mode** skips legacy schema gates but **not** user authorization (see
> [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md) P1).

### Legacy dialogs in the New System startup path

Status as of **2026-07-28**:

| Startup surface | New System path uses | Status |
| --- | --- | --- |
| Provisioning password prompt | `SiNet.App.Wpf.Admin.Security.ProvisioningPasswordWindow` (native) | Migrated |
| Vault setup / DB connection repair | `SiNetProjectManagerV2.WPF_Window.SecretSetupWindow` (legacy) | **Open** |

The vault/DB surface has not migrated because of a startup ordering constraint, not a missing screen.
The native `SecretSetupWindow` is DI-resolved; its `SecretSetupViewModel` requires
`AccControlPlaneStatusPresenter`, which requires `IAccProjectService` →
`ILocalAccProjectService` → `IDbContextFactory<SiNetSQLDbContext>`. That factory is registered in
`ConfigureServices` from a connection string the vault has to provide first, so the container cannot
exist at the moment the vault dialog needs to be shown.

Closing this requires one of: making the ACC status presenter optional on the native
`SecretSetupViewModel`, standing up a SQL-free bootstrap container for the pre-vault surface, or
moving the vault/DB gates to run after DI is built. All three change approved behavior and are
therefore tracked as an open decision rather than applied silently. Both legacy dialogs are marked
deprecated in source and stay in place for the Legacy path.

---

## 4. What is allowed to load in New system mode

- The **new shell** (`NewShellWindow` + `NewShellViewModel`) from `SiNet.App.Wpf`.
- The shared **Project Context** slice: `ICurrentProjectContext` (singleton), compact embeddable
  `ProjectSelectorView` + `ProjectSelectorViewModel` (host-configurable DPs — see `PROJECTS.md` §5),
  real read-only `IProjectQueryService`.
- The **current user** display via `ICurrentUserProfileService` (real name after P2 auth).
- Migrated Work Surfaces opened **on demand** from the shell menu:
  - **Email visual clone** via `IEmailWindowFactory.Create()`.
  - **Inspection shell** via DI-resolved `InspectionShellView` / `InspectionShellViewModel`.
  - **Native admin** (when authorized): user management, action permissions, keys/secrets (`SecretSetupWindow`).
- The already-built modular DI registrations (`AddSiNet*`, `AddSiNetSecrets`) that these surfaces depend on.

Everything above is resolved from the **existing** `App.ServiceProvider`; New system mode does not
build a second container.

---

## 5. What must not load in New system mode

- The legacy `MainWindow` (`SiNetProjectManagerV2.MainWindow`).
- The legacy menu bar / ribbon and any legacy menu-driven windows.
- Any legacy window opened only because `MainWindow` constructed it.
- Wholesale copies of legacy menu items (no scanning/porting the old menu en masse).

> **Known isolation limit (documented gap):** Both modes currently share the single
> `App.ServiceProvider` created in `ConfigureServices`. Services registered there are constructed as
> DI resolves them, so some host-level registrations still exist in memory in New system mode. The
> concrete win of this slice is that New system mode **does not open `MainWindow`** and **does not
> build the legacy menu/window graph**. Splitting the container (`AddSiNetClean()` vs
> `AddSiNetWithLegacyBridge()`) is tracked in `ARCHITECTURE_TARGET.md` "Composition TODOs" and is the
> path to fuller isolation.

---

## 6. Menu model for the new system

The shell menu is a **hierarchical, data-driven menu** (top groups + submenus), not a flat list and
not a legacy menu clone. Project selection is **not** in the shell chrome — surfaces that need it
(e.g. Email) host their own selector.

```plaintext
NewShellMenuItem
  Title            : display label (he-IL)
  Description      : optional tooltip / secondary text
  Children         : submenu items (empty for leaf actions)
  Open             : Action for leaf items (null for groups)
  IsAvailable      : bool — item is shown only when its surface exists in the new stack

Top groups (when they have children):
  פרויקטים ותבניות  → פתיחת פרויקט חדש, מיילים
  משימות             → לוח משימות, דוחות ביקורת, תהליכים, …
  משתמשים והרשאות    → ניהול משתמשים, הוספת משתמש, הרשאות פעולה
  מנהלה              → הגדרות, מפתחות, ACC, מצב מערכת, כלי פיתוח (DEBUG)
```

Rules:

- The menu is built **only** from surfaces that already exist in the refactored stack. Adding an item
  requires a real, DI-resolvable surface — no placeholders that throw.
- Menu items open surfaces through DI/factory paths. **Email** is hosted **inside** the shell content
  area via `IEmailSurfaceHost` (singleton, create-once — legacy `_cachedEmailManagementView` pattern).
  Re-opening **מיילים** via the menu calls `Show()` with no task context and **resets** the inbox to
  browse default (clears FollowQuote/task filters, banner, and list filters → Inbox). Task-driven
  opens pass an explicit `WorkSurfaceContext` and do not reset. Other surfaces may still open as
  separate windows until they are migrated to in-shell hosting.
- **Menu availability (P3):** `NewShellFactory` resolves whether an item is included/enabled via
  `IAuthorizationQueryService.CanCurrentUserAccessFeatureAsync` and `AppFeatureCodes` — not via legacy
  `CurrentUserContext` or `IsAdmin` checks inside `NewShellViewModel`.
- The menu carries **no business logic** and never mutates workflow (see §10 and
  `AI_DEVELOPMENT_GUIDE.md` rule 11).
- **Typography:** shell `Menu` / `MenuItem` use Stage 6 theme tokens (`SiFontFamily` +
  `SiTextNormalFontSize`) via implicit styles in `ThemeStyles.xaml`. Local
  `ItemContainerStyle` must `BasedOn` the implicit `MenuItem` style so command bindings
  do not drop theme fonts (see `SETTINGS.md` §9).

Initial menu (P3 + P6 + native admin):

| Item | Feature code | Min role | Opens |
| --- | --- | --- | --- |
| Email (inbox) | `Shell.OpenEmailSurface` | Employee | `IEmailSurfaceHost.Show()` → hosted `EmailSurfaceView` in shell content |
| Inspection (shell) | `Shell.OpenInspectionSurface` | Employee | DI-resolved `InspectionShellView` |
| User management | `Users.Manage` | Administrator | `UserListWindow` → native `UserManagementView` |
| Add user | `Users.Manage` | Administrator | `AddUserDialogWindow` → native `AddUserView` |
| Action permissions | `ActionPermissions.Manage` | Administrator | `ActionPermissionsWindow` → native `ActionPermissionsView` |
| Keys and secrets | `System.Settings.Write` | Administrator | `SecretSetupWindow` → native `SecretSetupView` |
| ACC status | `System.Settings.Write` | Administrator | `AccControlPlaneStatusWindow` → native ACC control/status + inbox reconciliation window |
| System health | Authenticated user | Any signed-in user | `SystemStatusWindow` → native subsystem status + background work |
| Workflow ops health | `Shell.OpenWorkflowOpsDashboard` | Administrator | `WorkflowOpsDashboardWindow` → read-only instance grid + stalled badge (complements System health; see [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md)) |
| ProjectType↔Workflow policy | `Shell.OpenProjectTypeWorkflowPolicy` | Administrator | Focused mapping admin (JobType → WorkflowDefinition). B2 target: continuation tracks are per JobType (`Project + Definition + JobType`) — see [`PROJECT_TYPE_WORKFLOW_POLICY.md`](./manual-tests/PROJECT_TYPE_WORKFLOW_POLICY.md) |
| Personal settings | Authenticated user | Any signed-in user | `ISettingsWindowFactory.CreatePersonal()` → native `SettingsWindow` (personal tabs) |
| System settings | `System.Settings.Write` | Administrator | `ISettingsWindowFactory.CreateSystemAdmin()` → native `SettingsWindow` (admin/global tabs) |
| MasterPlan monthly restore (planned DEV-018) | `Shell.OpenMasterPlanMonthlyRestore` | Management | Existing `--monthly`: restore `.bak` onto configured `Db_Mp_SiEng`, mismatch report vs current Replica, then replace `MP_*`. See [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md). **Not implemented until shipped from `development`.** |

Project Context (`ProjectSelectorView`) is **not** embedded in the `NewShellWindow` header.
Surfaces that need it (notably **Email**) host their own selector. The shell still owns
`ICurrentProjectContext` and reflects the Current Project in the **OS window title** via
`NewShellWindowTitle` (see §9) -- not via a header selector control.

**User management / add user (native):** Administrators see **ניהול משתמשים** and **הוספת משתמש** when
`Users.Manage` is authorized. Opens native `UserListWindow` / `AddUserDialogWindow` in `SiNet.App.Wpf.Admin.Users`
backed by `SqlUserManagementService` in Infrastructure.Sql — not legacy windows or SiNetSQL.MVVM.

**Action permissions (native):** Administrators see **הרשאות פעולה** when `ActionPermissions.Manage` is
authorized. Opens native `ActionPermissionsWindow` in `SiNet.App.Wpf.Admin.Permissions` backed by
`SqlActionPermissionAdminService` → `IActionPermissionAdminService` in Infrastructure.Sql.

**Keys and secrets (native, implemented):** Administrators see **מפתחות וסודות** when
`System.Settings.Write` is authorized. Opens native `SecretSetupWindow` in `SiNet.App.Wpf.Admin.Security`
(`SecretSetupView` + `SecretSetupViewModel`) backed by `CredentialVaultSecretSetupService` →
`ISecretSetupService` in `SiNet.Infrastructure.Secrets`. **Credential Vault is the single source of
truth** for all secret values — not `appsettings.json`, not repo files, not legacy `SecretSetupWindow`.

**ACC status (native, implemented):** Administrators also see **סטטוס ACC** when
`System.Settings.Write` is authorized. Opens native `AccControlPlaneStatusWindow` in
`SiNet.App.Wpf.Autodesk`, backed by the clean ACC control-plane seam. This window is **runtime-only**:
it shows mode, key metadata, known ACC project IDs, health, and diagnostics for the current host
process, and performs no settings writes or privileged ACC business orchestration. It also includes
a manual read-only `projectId + folderId + fileName` lookup tester through `IAccDocumentService`, a
derived ACC Docs URL that is shown only after a live resolve succeeds, and a read-only
`IAccInboxReconciliationService` panel for truth-based inbox inspection from the New System screen.

**System health (native, implemented):** Any authenticated user sees **מצב מערכת** (menu) and a
footer health indicator (colored dot + short summary including background-work count). Both open
native `SystemStatusWindow` in `SiNet.App.Wpf.Admin.SystemStatus`, backed by
`IRuntimeSubsystemStatusService` (aggregates external health via host adapter, ACC mode/probe,
Gmail connector auth, `IEmailAccBackgroundWorkTracker`, and the startup task registry). Display
states: `Running` | `Idle` | `Degraded` | `Stopped` | `NotConfigured`. Does **not** open legacy
`SystemHealthWindow`.

**Workflow ops health (native MVP, read-only):** Administrators see **בריאות תהליכים** when
`Shell.OpenWorkflowOpsDashboard` is authorized. Opens `WorkflowOpsDashboardWindow` in
`SiNet.App.Wpf.Admin.WorkflowOps`, backed by `IWorkflowQueryService` +
`IWorkflowRecoveryService.DetectStalledAsync` (+ infra strip from `IRuntimeSubsystemStatusService`).
Complements מצב מערכת; does **not** migrate WorkflowDashboard write, Retry, or Cancel. Full target:
[`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md).

Implemented capabilities in this surface:

| Capability | Port / service | Notes |
| --- | --- | --- |
| Vault read/write + validation | `ISecretSetupService.SaveAndValidateAsync` | 12 keys from `SecretCatalog` (includes AccService Certificate Password); post-save validators |
| **Export** `.secrets` | `ExportAsync` | AES-256-CBC + PBKDF2 encrypted file (legacy-compatible `SNET` format); never plain-text JSON |
| **Import** `.secrets` | `PreviewImportAsync`, `ImportAsync` | Preview shows key names only (no secret values); unknown keys skipped; overwrite requires confirmation |
| **AccService Generate** | `GenerateAccServiceApiKeyAsync` | 32-byte cryptographic random → Base64; saved to `SecretCatalog.AccServiceApiKey` |
| **AccService Test** | `TestAccServiceAsync` | Presence/format when `AccService:BaseUrl` unset; network `/v1/acc/diag` when configured |
| **Google materializer** | `IGoogleClientSecretsMaterializer` | Reads `SecretCatalog.GoogleClientSecrets` from Vault; writes validated JSON to `%LocalAppData%/SiNet/Secrets/google-client-secrets.json` for consumers that still need a file path |

**Google OAuth source of truth:** Vault → materialized temp file under `%LocalAppData%` → config fallback
(`Gmail:ClientSecretsPath` / `GoogleReports:ClientSecretsPath`) **only when Vault has no Google secret**,
with an explicit debug warning. The materialized file is a consumption artifact, not a second source of
truth. New System `GmailClientProvider` resolves paths via `IGoogleClientSecretsPathProvider` (vault-first).

**Settings (native, Stage 5 slice 2):** **הגדרות אישיות** opens for any authenticated user
(`ICurrentUserContext.UserId`) — per-user JSON, local logging, personal status colors. **הגדרות מערכת**
opens for administrators (`System.Settings.Write`) — global `SystemSettings`, central logging, global
status colors. Both use `SiNet.App.Wpf.Admin.Settings` — not legacy `SettingsWindow` or
`ManagementSettingsWindow`. See [`SETTINGS.md`](./SETTINGS.md) §5 (authorization policy).

---

## 7. How migrated windows are added to the new menu

To add a migrated window to the shell menu:

1. Ensure the surface is **DI-resolvable** (view + view-model registered via an `AddSiNet*`
   extension) or reachable through an existing factory.
2. Add a `NewShellMenuItem` in `NewShellViewModel` (or its menu builder) with a `Title` and an `Open`
   action that resolves the surface from `App.ServiceProvider` / the factory and shows it.
3. Guard availability: if a surface is host-only or not yet migrated, do **not** add it.
4. Record the addition in [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md) /
   [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) so the migration ledger stays accurate.

No legacy menu is scanned or copied. Each item is added deliberately, one migrated surface at a time.

---

## 8. Relationship to Current User

- The shell displays the **current authenticated user** (name) for context only.
- Identity source of truth stays in the host: the authenticated `SiNetSQL` user drives the display;
  the clean port `ICurrentUserContext` (`int? UserId`) is available to surfaces that must attribute an
  action to a real user (see `ARCHITECTURE_TARGET.md`).
- The shell is **not** an authorization authority. It does not grant/deny access; it only reflects who
  is signed in.
- Full identity, roles, action permissions, and user-management target rules:
  [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).

---

## 9. Relationship to Current Project

- The shell reuses the shared **Project Context** (`PROJECTS.md` §4/§5): a single app-wide
  `ICurrentProjectContext` (singleton) and the reusable `ProjectSelectorView`.
- Selecting a project in the shell updates the same context observed by every migrated surface
  (e.g. the Email clone), so the Current Project stays consistent across windows.
- **`NewShellViewModel.WindowTitle`** is bound to `NewShellWindow.Title` and reflects the selected
  project (`שיא חדש — מנהל פרויקטים — <version> — <number> — <name>` when available; base title with
  package version when none). Version comes from `SiNet.App.Wpf` `AssemblyInformationalVersion`
  (csproj `<Version>`), via `NewShellWindowTitle` — not hardcoded in XAML.
- WPF binds only to `ProjectSummaryDto`, never to EF entities.

---

## 10. Relationship to Workflow / Tasks / WorkSurfaceContext

- The shell is a **host surface**, not a workflow actor. It **must not** start, advance, auto-advance,
  or recover workflow instances (`AI_DEVELOPMENT_GUIDE.md` rule 11).
- Surfaces opened from the shell that are task-driven receive an explicit `WorkSurfaceContext` and use
  the official navigation/completion ports (`ITaskNavigationService`, task completion services). The
  shell menu itself only **opens** surfaces; it never hand-builds a workflow/task mutation.
- Task completion remains the only bridge back into workflow, via a task completion/coordinator
  service that may call `IWorkflowCommandService`.

### 10.1 Task workbench + complementary task-surface geometry

The **Tasks workbench** (`TaskWorkbenchView`) is a tall-narrow floating window docked to the
**right** of `SystemParameters.WorkArea` (default width `400`, see `DefaultNarrowWidth`).

Every **task-driven work surface** opened from that workbench (or from `IWorkSurfaceLauncher` with a
task id) must open in the **complementary rectangle** — the remaining WorkArea to the **left** of
the workbench strip:

| Window role | Geometry |
| --- | --- |
| Tasks workbench | Right strip: full WorkArea height, narrow width |
| Task surface (ProjectWork float, Email work-item, Inspection task window, launcher fallbacks) | Left: `WorkArea.Left` / `Top`; width = `WorkArea.Width − reservedRight`; height = WorkArea height |

Rules:

- Prefer the **live** workbench `ActualWidth` when the workbench window is open; otherwise reserve
  `TaskWorkbenchView.DefaultNarrowWidth`.
- Use `WindowStartupLocation=Manual` for these surfaces (not `CenterOwner` / `CenterScreen`).
- **Task hosts from the workbench** — including `OpenQuoteProjectDecisionDialog`,
  `SendQuoteToClientDialog`, Email work-item (`FileQuoteMaterial`), ProjectWork float, Inspection —
  use complementary geometry + `Owner=MainWindow` + `Activate()`, even when shown with `ShowDialog`.
- Small **prompts** only (place/company picker, MessageBox, create-task prompt) stay `CenterOwner`.
- Browse / shell-hosted inbox content stays in the main shell content area and is unchanged.

**Topmost / ownership (SOF-001):**

| Window role | `Topmost` | `Owner` |
| --- | --- | --- |
| Tasks workbench strip | may stay topmost (narrow chrome) | optional |
| Task **work surface** (ProjectWork float, Acc pop-out, Email work-item, Inspection task window) | **`false`** | **`MainWindow`** (shell-owned child) |
| Modal decision dialogs | N/A | owner window |

Work surfaces must not float above other desktop apps when the operator switches away from SiNet.
Complementary geometry is unchanged. Full dock into `IShellContentHost` for every task surface is
**out of scope** for this policy slice.

**Singleton / uniqueness (SOF-009):**

| Rule | Detail |
| --- | --- |
| At most one | Process-wide: only **one** task work surface open (Email work-item, ProjectWork float, Inspection, OpenQuote, SendQuote, QuoteClassification) |
| No duplicates | Same surface must not open twice with divergent state |
| Re-open | Same task → `Activate`; other task (any family) → close previous or rebind, then show |
| Workbench | `TaskWorkbenchView` is **not** a work surface — stays open for switching tasks |
| Focus | `Topmost=false` (SOF-001); `Owner=MainWindow` + `Activate`. Shell/workbench stay interactive (no global MainWindow disable) |

Implementation: `TaskSurfaceWindowLayout` in `SiNet.App.Wpf.Surfaces.Tasks`;
`ITaskSurfaceWindowCoordinator` + `ProjectWorkTaskFloatingHost` / Email / Inspection hosts / Acc pop-out.

---

## 11. Settings mechanism (Stage 5 — logging ports)

Full inventory: [`SETTINGS.md`](./SETTINGS.md).

Two settings mechanisms exist today:

| Mechanism | Scope | Storage | Windows | Notes |
| --- | --- | --- | --- | --- |
| `AppSettings` via `SettingsManager` | **Per-user** | **File** — `%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json` | `SettingsWindow` | UI/theme, logging on/off + log directory. |
| `SystemSettingsService` (SiNetSQL) | **Global** | **DB** — `SystemSettings` keyed by `SystemSettingKeys` | `ManagementSettingsWindow` | Includes `Logging.*` keys for central logging. |
| `appsettings.json` | **Per-machine/deploy** | File next to exe | — | Bootstrap config (connection string, etc.). |

**Stage 5 ports + native UI (implemented):**

```plaintext
IAppSettingsService              → per-user JSON (JsonAppSettingsService)
ISystemSettingsQueryService      → global SystemSettings (SqlSystemSettingsService)
ISystemSettingsCommandService    → admin write
ILoggingSettingsQuery/Command    → logging slice (same SQL adapter)
IStatusColorSettingsService      → status color tables
ILoggingRuntimeApplier           → host applies user logging toggle
```

Native **הגדרות אישיות** + **הגדרות מערכת** — `SettingsWindow` in `SiNet.App.Wpf/Admin/Settings`
(personal vs admin menu entries; see `SETTINGS.md` §5).

**Stage 6 theme:** per-user typography/colors via `IThemeRuntimeApplier` — see `SETTINGS.md` §9
(incl. shell menu typography, tree/KPI wiring, and Phase 4 product-fixed semantic/state brushes).

Guardrails: reads/writes behind Application ports; no schema/migrations; `SiNet.App.Wpf` does not touch
legacy settings types directly.

---

## 12. Guardrails

New system mode / shell work must **not**:

```plaintext
- open the legacy MainWindow in New system mode
- copy legacy menus wholesale or scan the old menu
- load all legacy windows
- connect DB writes
- add EF migrations
- modify ModelSnapshot
- modify DbContext / DbSet mappings
- change schema
- mutate workflow from the UI
- make feature ViewModels own shell/menu/settings logic
- put WPF types outside SiNet.App.Wpf
```

It **must**:

```plaintext
- treat SiNet.App.Wpf / NewShellWindow as the production desktop path
- open NewShellWindow as Current.MainWindow so OnMainWindowClose shutdown still works
- resolve every migrated surface from the existing App.ServiceProvider / factories
- register with modular AddSiNet* extensions
- keep the menu data-driven and migrated-only
- keep V2 Legacy path available only as in-repo reference (not publish)
```

---

## 13. Migration sequence (Historical Snapshot -- slices already largely landed)

```plaintext
Slice 1 (Historical)
  1. Startup mode option on V2 hybrid path.
  2. Legacy mode unchanged when unchecked (V2 only).
  3. New system mode opens NewShellWindow (not MainWindow).
  4. Shell shows a minimal, migrated-only menu.
  5. Menu opens Email via IEmailWindowFactory / IEmailSurfaceHost.
  6. Menu opens Inspection shell via DI.
  7. Settings not required for shell boot.

Slice 2 (Stage 5 -- settings) -- Implemented
  - Native Settings UI with personal/admin split (see SETTINGS.md §5).

Slice 3+ (ongoing)
  - Add migrated surfaces to the shell one at a time (recorded in the migration map).
  - Split composition (AddSiNetClean vs AddSiNetWithLegacyBridge) to deepen isolation.
  - Grow the shell toward the Workflow-first Work Surface model once parity surfaces exist.
```

---

## 14. Out of Scope

- Re-introducing `SiNetProjectManagerV2` as a publish channel
- Putting `ProjectSelectorView` into the `NewShellWindow` header chrome
- Deleting the V2 Legacy path from the repo without explicit approval
- Code / XAML / DI changes in a documentation-only round

---

## 15. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Dual-mode as office production model | Dropped as target | Production = App.Wpf only |
| ProjectSelector in shell header | Dropped / Wrong As-Is | Email (and other surfaces) embed the selector |
| Publishing V2 MSIX | Dropped | Cutover to App.Wpf |

---

## 16. Needs Review

1. Whether pilot PCs actually run App.Wpf **1.0.23** from the UNC share (ops verify).
2. Whether any operator still launches V2 hybrid by mistake (should not).
3. Vault/DB `SecretSetupWindow` still on legacy type in New System startup -- open decision in §3.

> Reconciliation: [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md).
