# SiNet Application Shell (Legacy mode vs New system mode)

> **Status:** Draft — Shell/Startup Round (2026-06-27)
> **Working branch:** `SiWorkNet10`
> **New solution:** `SiNet.sln` · **Legacy/functional reference:** `SiNetProjectManager.sln`

This is the **target** document for the SiNet application **shell** and **startup mode selection**.
It refines [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) and
[`MIGRATION_MAP.md`](./MIGRATION_MAP.md) for the specific problem of **isolating the refactored app
from the legacy host at startup**. When code and this document disagree, fix the document first,
then the code (see [`README`](./README.md) documentation-driven workflow).

> Related target docs: [`PROJECTS.md`](./PROJECTS.md) (Project Context, reused by the shell),
> implemented by [`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md).

---

## 1. Purpose of the application shell

The **application shell** is the top-level window that hosts the refactored ("new system") surfaces
without dragging in the legacy host. It exists to:

- Provide a **clean entry point** for the migrated stack (`SiNet.App.Wpf`) that does **not** open the
  legacy `MainWindow`, its menu graph, or its window/services graph.
- Make **startup performance measurable in isolation**: if the new shell is fast and the legacy host
  is slow, the slowdown is in legacy startup; if both are slow, the cost is shared (DI/composition).
- Give migrated Work Surfaces (Email, Inspection, Project Context, later Settings) a home as they are
  ported, following the *Work Surfaces* model in `ARCHITECTURE_TARGET.md` §4.

The shell is **additive**. It never replaces the legacy host in Legacy mode and never changes legacy
behavior.

---

## 2. Legacy mode vs New system mode

```plaintext
Legacy mode      → current production behavior, unchanged
				 → opens the legacy MainWindow (SiNetProjectManagerV2)
				 → all legacy menus, services, windows load as today

New system mode  → opens the new clean shell (NewShellWindow, SiNet.App.Wpf)
				 → shows ONLY migrated/refactored menu items
				 → does NOT open the legacy MainWindow
				 → loads only new/refactored surfaces on demand
```

The mode is chosen at startup in **`StartupModeSelectionWindow`** — the **first visible UI**, before
credential vault, database gate, schema validation, or splash. Default selection is **New System**.

Rules:

- **One prompt only** — `ShowSplashThenMainWindow()` does **not** ask for mode again (Legacy: splash →
  `MainWindow` only).
- **New System** builds `ServiceProvider` via `ConfigureServices()` **before** opening `NewShellWindow`
  (DI required for `INewShellFactory`, Project Context, Email/Inspection factories). It **skips**
  legacy DB retry loop, schema validation, debug role selector, and user authorization gates.
- **No silent Legacy fallback** — if New System shell creation fails, show an error and **shutdown**;
  do not open `MainWindow` in the background.

> **Non-goal / anti-pattern (explicit):** New system mode must **not** be implemented by opening the
> legacy `MainWindow` and hiding menu items. That would still load the legacy system and defeat the
> performance-isolation goal.

---

## 3. Startup flow

The legacy host (`SiNetProjectManagerV2`) remains the process entry point and the composition root
for now. Startup is **code-driven** (no XAML `StartupUri`) in `App.xaml.cs`.

```plaintext
App.OnStartup
  → ShutdownMode = OnExplicitShutdown
  → ConfigureGlobalHandlers
  → Load AppSettings + ApplySettings
  → StartupModeSelectionWindow (FIRST visible UI; default = New System)
        ├─ cancel / close → Shutdown
        ├─ New System   → SetupCredentialVault (if needed for conn string)
        │                 → ConfigureLoggingAndSettings
        │                 → ConfigureServices + WireLegacyLocators
        │                 → LaunchNewSystemShell() → NewShellWindow
        │                 (no schema/auth gates; no MainWindow; no second prompt)
        └─ Legacy       → credential vault → DB gate → schema → auth → splash → MainWindow
                          (ShowSplashThenMainWindow: splash only, no mode prompt)
```

The mode prompt is shown **before any legacy gate** so New system mode can skip credential/DB/schema
dialogs entirely. The choice is captured as a `StartupMode` value and routed by a small, unit-testable
helper (`StartupModeRouter`) so the decision can be tested without WPF.

> Current-user selection: in **both modes**, startup authorizes the current **Windows identity**
> against `SIUser` (`AuthorizeCurrentUser`) after optional DEBUG role-selector when enabled.
> **New system mode** skips legacy schema gates but **not** user authorization (see
> [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md) P1).

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

The shell menu is a **data-driven list of migrated items**, not a legacy menu clone.

```plaintext
NewShellMenuItem
  Title            : display label (he-IL)
  Description      : optional tooltip / secondary text
  Open             : Action invoked to open the surface (resolves from DI/factory)
  IsAvailable      : bool — item is shown only when its surface exists in the new stack
```

Rules:

- The menu is built **only** from surfaces that already exist in the refactored stack. Adding an item
  requires a real, DI-resolvable surface — no placeholders that throw.
- Menu items open surfaces through the **same** DI/factory paths the legacy host already uses
  (`IEmailWindowFactory`, `InspectionShellView`), so behavior is identical to the reviewed clones.
- **Menu availability (P3):** `NewShellFactory` resolves whether an item is included/enabled via
  `IAuthorizationQueryService.CanCurrentUserAccessFeatureAsync` and `AppFeatureCodes` — not via legacy
  `CurrentUserContext` or `IsAdmin` checks inside `NewShellViewModel`.
- The menu carries **no business logic** and never mutates workflow (see §10 and
  `AI_DEVELOPMENT_GUIDE.md` rule 11).

Initial menu (P3 + P6 + native admin):

| Item | Feature code | Min role | Opens |
| --- | --- | --- | --- |
| Email (visual clone) | `Shell.OpenEmailSurface` | Employee | `IEmailWindowFactory.Create()` |
| Inspection (shell) | `Shell.OpenInspectionSurface` | Employee | DI-resolved `InspectionShellView` |
| User management | `Users.Manage` | Administrator | `UserListWindow` → native `UserManagementView` |
| Add user | `Users.Manage` | Administrator | `AddUserDialogWindow` → native `AddUserView` |
| Action permissions | `ActionPermissions.Manage` | Administrator | `ActionPermissionsWindow` → native `ActionPermissionsView` |
| Keys and secrets | `System.Settings.Write` | Administrator | `SecretSetupWindow` → native `SecretSetupView` |
| ACC status | `System.Settings.Write` | Administrator | `AccControlPlaneStatusWindow` → native runtime-only ACC status window |
| Personal settings | Authenticated user | Any signed-in user | `ISettingsWindowFactory.CreatePersonal()` → native `SettingsWindow` (personal tabs) |
| System settings | `System.Settings.Write` | Administrator | `ISettingsWindowFactory.CreateSystemAdmin()` → native `SettingsWindow` (admin/global tabs) |

Project Context (`ProjectSelectorView`) is embedded in the shell header — not a menu item.

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
process, and performs no settings writes or privileged ACC operations. It also includes a manual
read-only `projectId + folderId + fileName` lookup tester through `IAccDocumentService`.

Implemented capabilities in this surface:

| Capability | Port / service | Notes |
| --- | --- | --- |
| Vault read/write + validation | `ISecretSetupService.SaveAndValidateAsync` | 11 keys from `SecretCatalog`; post-save validators |
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
  project (`SiNet Project Manager — New System — <number> — <name>` when available; base title when none).
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

**Stage 6 theme:** per-user typography/colors via `IThemeRuntimeApplier` — see `SETTINGS.md` §9.

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
- keep Legacy mode behavior identical when the checkbox is unchecked
- open NewShellWindow as Current.MainWindow so OnMainWindowClose shutdown still works
- resolve every migrated surface from the existing App.ServiceProvider / factories
- register with modular AddSiNet* extensions
- keep the menu data-driven and migrated-only
```

---

## 13. Migration sequence

```plaintext
Slice 1 (this round)
  1. Add startup mode option to the first login/user-selection moment.
  2. Legacy mode unchanged when unchecked.
  3. New system mode opens NewShellWindow (not MainWindow).
  4. Shell shows a minimal, migrated-only menu.
  5. Menu opens Email visual clone via IEmailWindowFactory.
  6. Menu opens Inspection shell (if available) via DI.
  7. Do not migrate settings unless required for the shell to run (it is not).

Slice 2 (Stage 5 — settings)
  - Native Settings UI with personal/admin split (see SETTINGS.md §5).
  - Personal menu for authenticated users; system menu for System.Settings.Write.

Slice 3+
  - Add migrated surfaces to the shell one at a time (recorded in the migration map).
  - Split composition (AddSiNetClean vs AddSiNetWithLegacyBridge) to deepen isolation.
  - Grow the shell toward the Workflow-first Work Surface model once parity surfaces exist.
```
