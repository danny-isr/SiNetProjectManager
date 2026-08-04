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
| ProjectWork | `ProjectWork.ScanExclusionRules` | Native Settings (DEV-006) |
| Inspection | `InspectionTemplatesFolderId`, `InspectionReportsFolderId`, `ReportsOutputRoot`, `StampTemplatePath` | ManagementSettings |
| Status labels | `StatusLabel_*` | ManagementSettings |
| AI | `Ollama*`, `AiModel.*`, `AiProvider.*`, `AiConfiguredCloudModels` | ManagementSettings + AiModelCatalog |
| Logging | `Logging.*` | ManagementSettings |

**Deferred in native UI (stored only):** User Groups button, Google folder validate, ACC template refresh, AiModelCatalogWindow.

`AccService.BaseUrl` validation mirrors legacy expectations: empty = local mode; otherwise it must
be an absolute `http`/`https` URL and is normalized without a trailing slash on save.

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

The `ACC (גלובלי)` tab also shows a read-only **runtime ACC status panel** from the clean
control-plane seam. That panel reflects the **current host process** (mode, key metadata, known ACC
project IDs, health, diag) and is intentionally separate from the editable stored
`AccService.BaseUrl` field.

---

## 7. Boundaries

No schema/migrations. No Serilog bootstrap changes. Settings stored for features not yet migrated are **display + persist only**.

Tests: `SettingsStage5BoundaryTests.cs`, `NativeSettingsSurfaceTests.cs`, `ThemeStage6Tests.cs`.

---

## 9. Theme / Typography policy (Stage 6)

Per-user appearance in `settings.json` drives **dynamic WPF resources** — no restart required.

### Typography levels

Base size: `BaseFontSize` (legacy JSON key `FontSize` still read/written).

| Level | Scale default | Validation range |
| --- | --- | --- |
| TextTiny | 0.80× | 0.60–0.95 |
| TextSmall | 0.90× | 0.70–1.00 |
| TextNormal | 1.00× | 0.90–1.10 |
| TextMedium | 1.20× | 1.05–1.35 |
| TextLarge | 1.50× | 1.30–1.80 |
| TextHuge | 1.80× | 1.60–2.40 |

Resolved sizes: `ThemeCalculator.Compute()` → applied to Application resources.

### Theme colors (per-user JSON)

| Field | Default |
| --- | --- |
| `PrimaryColor` | `#1F3A5F` |
| `SecondaryColor` | `#757575` |
| `ForegroundColor` | `#000000` |
| `BackgroundColor` | `#FFFFFF` |

Settings UI: color picker + preview swatch + hex (secondary) + reset.

### Resource keys (`SiNet.Application.Settings.ThemeResourceKeys`)

Font: `SiFontFamily`, `SiTextTinyFontSize` … `SiTextHugeFontSize`

Brushes (appearance JSON / runtime-applied): `SiPrimaryBrush`, `SiSecondaryBrush`, `SiBackgroundBrush`, `SiForegroundBrush`

Brushes (static structural / semantic defaults — not yet in appearance JSON):

| Key | Default | Role |
| --- | --- | --- |
| `SiBorderBrush` | `#E5E7EB` | Borders / dividers |
| `SiMutedForegroundBrush` | `#6B7280` | Secondary text |
| `SiSurfaceBrush` | `#F7F8FA` | Secondary panels |
| `SiOnPrimaryBrush` | `#FFFFFF` | Text/icons on primary or brand bars |
| `SiDangerBrush` | `#DC2626` | Error / danger accents |
| `SiWarningBrush` | `#D97706` | Warning accents |
| `SiSuccessBrush` | `#059669` | Success accents |

Styles: `SiTextTinyStyle` … `SiTextHugeStyle`, `SiRoundedButtonBase` (CornerRadius 6), implicit `Button`,
`SiPrimaryButtonStyle`, `SiSecondaryButtonStyle`, `SiTextBoxStyle`, `SiComboBoxStyle`, `SiSectionHeaderStyle`

XAML dictionaries: `SiNet.App.Wpf/Theme/TypographyResources.xaml`, `BrushResources.xaml`, `ThemeStyles.xaml`.

**V2 host:** production runs under `SiNetProjectManagerV2` — theme XAML is **not** in V2 `App.xaml`. `ThemeResourceLoader.EnsureApplicationResourcesMerged()` merges dictionaries into `Application.Current.Resources` at New System startup and before shell/native windows open.

### Runtime

| Port / component | Implementation |
| --- | --- |
| `IThemeRuntimeApplier` | `WpfThemeRuntimeApplier` (updates dynamic font/brush keys) |
| `ThemeResourceLoader` | Merges theme XAML into Application resources (V2 host) |
| Startup | `ThemeStartupInitializer` in New System pipeline (after auth) |
| Save | `SettingsViewModel` → `IAppSettingsService` (persist only; theme already live) |
| Live preview | `SettingsViewModel` → `IThemeRuntimeApplier` on every appearance change (sliders immediately; colors when hex valid; color picker sliders before OK) |
| Color picker | `WpfColorPickerDialog` — RGB + Brightness (-100…+100) sliders; preview callback before OK; Cancel restores pre-dialog hex |
| Status colors | Personal/global status tabs use `ThemeColorEditor` (swatch + picker + reset; hex secondary) |
| Reload | Re-reads JSON, updates UI + snapshot, applies theme immediately via `IThemeRuntimeApplier` |
| Cancel / close without save | `RollbackAppearanceIfNeeded()` restores `_originalAppearance` snapshot |
| Startup | `ThemeStartupInitializer` loads saved JSON → `IThemeRuntimeApplier` |

Logging applier remains separate — appearance preview/save does **not** call `ILoggingRuntimeApplier`.

**Live preview policy:** while the personal Settings window is open, appearance edits apply immediately to all windows using `DynamicResource`. Color picker RGB sliders preview before OK; Cancel in the picker restores the pre-dialog color (and theme). Save writes JSON only. Reload re-applies the saved JSON to all windows and resets the rollback snapshot. Cancel or closing Settings without save rolls back to the snapshot taken at the last load/save. Startup loads the persisted values via `ThemeStartupInitializer`.

### Connected native surfaces (Stage 6)

`NewShellWindow`, `ProjectSelectorView`, `ProjectWorkWindowView`, User Management, Add User, Action Permissions, Secret Setup, `SettingsView`/`SettingsWindow`, `SystemStatusWindow`, `InspectionShellView`, Email visual clone (**content** areas beyond title chrome), Inspection visual clone (non-brand chrome), `TaskWorkbenchView`, quote dialogs, `ResetOptionsDialog`, `ExternalDownloadBrowserWindow`, `ProvisioningPasswordWindow`, MasterPlan mapping + R01/R02/R03 report windows, Workflow canvas/closed viewer (non-semantic chrome), `StartupModeSelectionWindow` (theme buttons + SiNet mark). Host windows use `ThemeWindowChrome.ApplyThemedWindowBackground`.

**Intentional exceptions (keep hardcoded):**

- Email/Inspection **title-bar brand chrome** (`#1976D2` / `#2E7D32` + on-primary text)
- Splash surfaces (`StartupSplashWindow`, V2 `SplashWindow`) use fixed SiNet brand teal `#0B6E99` (not live theme)
- Semantic row tints in User Management DataGrid
- Workflow **node/legend** colors and status chips (email / quote / inspection banners)
- Tree state colors in ProjectWork (physical / missing / type / empty)
- `SiCardStyle` (still deferred)
- Legacy V2 windows outside New System

---

## 8. Remaining work

| Item | Notes |
| --- | --- |
| Google folder validate / ACC template refresh | Needs migrated Google/ACC ports |
| User Groups admin | Legacy `UserGroupManagementWindow` |
| AiModelCatalogWindow parity | CSV field exposed; catalog UI deferred |
| Appearance runtime in New System | **Done (Stage 6)** — `IThemeRuntimeApplier` + `WpfThemeRuntimeApplier` |
| Bootstrap log path unification | See §2.4 in prior slice docs / `LOGGING.md` |
