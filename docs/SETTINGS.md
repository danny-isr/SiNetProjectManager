# Settings — New System (Stage 5)

> **Status:** Stage 5 slice 2 (2026-07-03) — full settings ports + native Settings UI.
> **Related:** [`LOGGING.md`](./LOGGING.md), [`APP_SHELL.md`](./APP_SHELL.md) §11.

---

## 1. Goal

All settings (per-user JSON + global DB + status colors) behind Application ports. Native **הגדרות**
in New System replaces legacy `SettingsWindow` + `ManagementSettingsWindow` for the NewShell menu.

`SiNet.App.Wpf` must **not** reference legacy settings types, Serilog, or `AppLogger`.

---

## 2. Legacy inventory (reference)

### 2.1 Per-user — `%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json`

| JSON key (PascalCase) | Default | Runtime / restart |
| --- | --- | --- |
| `FontFamily` | Segoe UI | Runtime (legacy `ApplySettings`) |
| `FontSize` | 12 | Runtime |
| `ForegroundColor` / `BackgroundColor` | #000 / #FFF | Runtime |
| `AllowMultipleInstances` | true | **Restart** |
| `LoggingEnabled` | false | **Runtime** via `ILoggingRuntimeApplier` |
| `LogDirectory` | "" | Partial runtime |
| `FloatingWindowActiveOpacity` / `IdleOpacity` | 1.0 / 0.7 | Runtime (floating windows) |
| `FloatingTasks*` / `FloatingInspection*` geometry | NaN / defaults | Next window open |
| `EnableAuthorizationTestMode` | false | **Restart** (no legacy UI) |

### 2.2 Global — `dbo.SystemSettings`

All keys mirrored in `SiNet.Application.Settings.SystemSettingKeys` + `LoggingSettingKeys`.
Defaults in `SystemSettingsDefaults`.

| Group | Keys | Legacy UI |
| --- | --- | --- |
| Email/office | `DefaultProjectTitle`, `OfficeManagementProjectId`, `HourPriceDefault`, `InboxFolderName`, `InboxProjectName`, `AccViewerMaxTabs` | ManagementSettings |
| ACC | `AccService.BaseUrl`, `AccBootstrapAdminEmail`, `AccProjectTemplateName`, `AccManualUploadAllowedExtensions` | ManagementSettings |
| Inspection | `InspectionTemplatesFolderId`, `InspectionReportsFolderId`, `ReportsOutputRoot`, `StampTemplatePath` | ManagementSettings |
| Status labels | `StatusLabel_*` | ManagementSettings |
| AI | `Ollama*`, `AiModel.*`, `AiProvider.*`, `AiConfiguredCloudModels` | ManagementSettings + AiModelCatalog |
| Logging | `Logging.*` | ManagementSettings |

**Deferred in native UI (stored only):** User Groups button, Google folder validate, ACC template refresh, AiModelCatalogWindow.

### 2.3 Status colors (separate tables)

| Store | Table | Scope |
| --- | --- | --- |
| Personal override | `UserStatusPreference` | Per-user |
| Global default | `ProjectAssignmentStatus.DefaultColorHex` | Admin |

Port: `IStatusColorSettingsService`.

---

## 3. Application ports

Location: `src/SiNet.Application/Settings/`

| Port | Purpose |
| --- | --- |
| `IAppSettingsService` | Full per-user JSON (`UserAppSettingsDto`) |
| `ISystemSettingsQueryService` | Read all global settings (`SystemSettingsDto`) |
| `ISystemSettingsCommandService` | Admin write global settings |
| `ILoggingSettingsQueryService` / `ILoggingSettingsCommandService` | Logging slice (same SQL adapter) |
| `ILoggingRuntimeApplier` | Apply user logging toggle at runtime (host) |
| `IStatusColorSettingsService` | User overrides + global default colors |

---

## 4. Infrastructure adapters

| Adapter | Module | Storage |
| --- | --- | --- |
| `JsonAppSettingsService` | `SiNet.Infrastructure.Logging` | `settings.json` — merge write, preserves unknown fields |
| `SqlSystemSettingsService` | `SiNet.Infrastructure.Sql` | All managed `SystemSettings` keys |
| `SqlStatusColorSettingsService` | `SiNet.Infrastructure.Sql` | Status color tables |
| `LegacyLoggingRuntimeApplier` | `SiNetProjectManagerV2` | `AppLogger.Configure` |

Registration:

- `AddSiNetUserLoggingSettings()` → `IAppSettingsService`
- `AddSiNetSystemSettingsSql()` → system + logging + status color ports
- `AddSiNetSettingsAdminWpf()` → native UI

Wired in `AddSiNetNewSystemGraph()`.

---

## 5. Authorization policy (slice 2)

Settings are split by **storage** and **who may view/edit**:

| Category | Storage | View | Edit | Menu |
| --- | --- | --- | --- | --- |
| Personal appearance / behavior / floating | `settings.json` | Any authenticated user | Same | **הגדרות אישיות** |
| Local logging toggle + path | `settings.json` | Authenticated user | Same; **runtime** via `ILoggingRuntimeApplier` | **הגדרות אישיות** |
| User status colors | `UserStatusPreference` | Owner user | Owner user | **הגדרות אישיות** |
| Global / admin settings | `SystemSettings` DB | `System.Settings.Write` | Same | **הגדרות מערכת** |
| Central / server logging | `SystemSettings` `Logging.*` | Admin | Admin; **restart required** for bootstrap consumers | **הגדרות מערכת** |
| Global status colors | `ProjectAssignmentStatus` | Admin | Admin | **הגדרות מערכת** |

**Important:** Bootstrap reads (Serilog central path, retention, levels) require **application restart** to take effect.
Storage remains JSON/DB — there is no separate “bootstrap storage”.

### ViewModel flags

`SettingsViewModel` exposes:

- `CanViewPersonalSettings` / `CanEditPersonalSettings`
- `CanViewSystemSettings` / `CanEditSystemSettings`
- `CanViewGlobalStatusColors` / `CanEditGlobalStatusColors`

Two shell menu entries open the same `SettingsWindow` with different `SettingsSurfaceScope`:

- `Personal` — personal tabs only
- `SystemAdmin` — admin/global tabs only

Save is split: personal scope → `IAppSettingsService` (+ runtime applier for local logging); system scope → `ISystemSettingsCommandService` only.

---

## 6. Native UI

| Component | Location |
| --- | --- |
| Window | `SiNet.App.Wpf/Admin/Settings/SettingsWindow.cs` |
| View | `SettingsView.xaml` (TabControl sections) |
| ViewModel | `SettingsViewModel.cs` |

Menu **הגדרות אישיות** — any authenticated user (`ICurrentUserContext.UserId`).
Menu **הגדרות מערכת** — gated by `System.Settings.Write` (Administrator).

Save / Reload / Cancel. Central log path probe via `ILoggingSettingsCommandService.ProbeCentralLogPathAsync`.

---

## 7. Boundaries

No schema/migrations. No Serilog bootstrap changes. Settings stored for features not yet migrated are **display + persist only**.

Tests: `SettingsStage5BoundaryTests.cs`, `NativeSettingsSurfaceTests.cs`.

---

## 8. Remaining work

| Item | Notes |
| --- | --- |
| Google folder validate / ACC template refresh | Needs migrated Google/ACC ports |
| User Groups admin | Legacy `UserGroupManagementWindow` |
| AiModelCatalogWindow parity | CSV field exposed; catalog UI deferred |
| Appearance runtime in New System | Stored; legacy `ApplySettings` still host-owned |
| Bootstrap log path unification | See §2.4 in prior slice docs / `LOGGING.md` |
