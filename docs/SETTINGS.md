# Settings — New System target (Stage 5)

> **Status:** Stage 5 in progress (2026-07-03) — logging settings ports + adapters; native Settings UI later.
> **Related:** [`LOGGING.md`](./LOGGING.md), [`APP_SHELL.md`](./APP_SHELL.md) §11, legacy ops [`SiNetProjectManagerV2/Docs/LOGGING.md`](../SiNetProjectManagerV2/Docs/LOGGING.md).

---

## 1. Goal

Move settings behind clean Application ports so **New System** (`SiNet.App.Wpf`) never references
legacy `AppSettings`, `SettingsManager`, `AppLogger`, Serilog, or `CentralLoggingSettings` directly.

Stage 5 **starts with logging-related settings** (local toggle/path + central/server DB keys).
Native **הגדרות** UI comes **after** ports and adapters are stable — distinct from **מפתחות וסודות**.

---

## 2. What exists today (legacy)

### 2.1 Per-user file settings

| Item | Location |
| --- | --- |
| POCO | `SiNetProjectManagerV2/AppSettings.cs` |
| Persistence | `SiNetProjectManagerV2/SettingsManager.cs` |
| File | `%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json` |
| Legacy read-once | `{exe dir}\settings.json` (migrated, never written) |
| UI | `SiNetProjectManagerV2/WPF Window/SettingsWindow.xaml(.cs)` |

**Logging fields in JSON:**

| JSON key | C# property | Default | Pipeline effect |
| --- | --- | --- | --- |
| `loggingEnabled` | `LoggingEnabled` | `false` | Sets `AppLogger.FileLevelSwitch` → local file min `Debug` vs `Error` |
| `logDirectory` | `LogDirectory` | `""` | Updates `AppLogger.LogDirectory` for UI utilities; **Serilog local sink path is fixed at static bootstrap** unless host is refactored |

**Other fields in same file (future `IAppSettingsService` slices):** font/colors,
`allowMultipleInstances`, floating window geometry, DEBUG auth test mode.

### 2.2 Global / DB settings (central logging)

| Item | Location |
| --- | --- |
| Table | `dbo.SystemSettings` (`SettingKey`, `SettingValue`, …) |
| Service | `SiNetSQL/Services/SystemSettingsService.cs` |
| Keys | `SiNetSQL/Services/SystemSettingKeys.cs` — `Logging.*` prefix |
| Loader | `SiNetSQL/Services/Logging/CentralLogging.cs` — `CentralLoggingSettings.LoadFromDatabase` |
| Admin UI | `ManagementSettingsWindow.xaml(.cs)` |

**All DB keys affecting the logging pipeline:**

| DB key | Scope | Local file | Central share | Applied |
| --- | --- | --- | --- | --- |
| `Logging.CentralLogPath` | Global | — | Enable + UNC root | **Next restart** |
| `Logging.LocalRetentionDays` | Global | Retention | — | Next restart |
| `Logging.CentralRetentionDays` | Global | — | Retention | Next restart |
| `Logging.Client.FileLevel` | Global | Default min level | — | **Overridden on WPF client** by `FileLevelSwitch` at runtime |
| `Logging.Client.CentralLevel` | Global | — | Client central min level | Next restart |
| `Logging.AccService.FileLevel` | Global | AccService local | — | Next AccService restart |
| `Logging.AccService.CentralLevel` | Global | — | AccService central | Next AccService restart |
| `Logging.SyncEngine.FileLevel` | Global | SyncEngine local | — | Next SyncEngine restart |
| `Logging.SyncEngine.CentralLevel` | Global | — | SyncEngine central | Next SyncEngine restart |

### 2.3 Host bootstrap (unchanged in Stage 5)

`SiNetProjectManagerV2/App.xaml.cs` static ctor:

1. `_logDir = GetLogDirectory()` → `%LOCALAPPDATA%\SiNet\SiNetProjectManagerV2\Logs`
2. `CentralLoggingSettings.LoadFromDatabase(..., SiNetApp.Client, localFileLevelSwitch: AppLogger.FileLevelSwitch)`
3. Serilog `Log.Logger` + `AddSiNetCentralLogging`

After settings load: `ConfigureLoggingAndSettings()` → `AppLogger.Configure(LoggingEnabled, LogDirectory)`.

### 2.4 Local log path inconsistency (documented gap)

| Source | Path |
| --- | --- |
| Serilog bootstrap (`App.GetLogDirectory`) | `%LOCALAPPDATA%\SiNet\SiNetProjectManagerV2\Logs` |
| `AppLogger.GetDefaultLogDirectory()` / Settings UI hint | `%LOCALAPPDATA%\SiNetProjectManager\Logs` |

Stage 5 ports expose both **bootstrap default** and **AppLogger default** in DTO metadata; unification is a later bootstrap refactor.

---

## 3. Application ports (Stage 5)

Location: `src/SiNet.Application/Settings/`

| Port | Purpose |
| --- | --- |
| `IAppSettingsService` | Per-user settings — **logging slice first** (`UserLoggingSettingsDto`) |
| `ILoggingSettingsQueryService` | Read global central logging from DB |
| `ILoggingSettingsCommandService` | Admin write global central logging (requires `System.Settings.Write`) |
| `ILoggingRuntimeApplier` | Host applies user logging toggle to live pipeline (`AppLogger.Configure`) |

DTOs: `UserLoggingSettingsDto`, `CentralLoggingSettingsDto`, `AppLogLevelsDto`, `LogLevelDto`.

**No** legacy types in Application or App.Wpf.

---

## 4. Infrastructure adapters (Stage 5)

| Adapter | Module | Storage |
| --- | --- | --- |
| `JsonUserLoggingSettingsService` | `SiNet.Infrastructure.Logging` | `%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json` (merge JSON; preserves non-logging keys) |
| `SqlLoggingSettingsService` | `SiNet.Infrastructure.Sql` | `SystemSettings` rows where `SettingKey LIKE 'Logging.%'` |
| `LegacyLoggingRuntimeApplier` | `SiNetProjectManagerV2` (host) | Calls `AppLogger.Configure` — **not** referenced from App.Wpf |

Registration: `AddSiNetUserLoggingSettings()` in `LoggingServiceCollectionExtensions`, `AddSiNetLoggingSettingsSql()` in Sql; wired in `AddSiNetNewSystemGraph()`.

---

## 5. New System UI (deferred)

| Menu item | Status |
| --- | --- |
| **מפתחות וסודות** | Implemented — `SecretSetupWindow` (Credential Vault) |
| **הגדרות** | Placeholder disabled in `NewShellFactory` until native Settings surface exists |

Native Settings will bind to `IAppSettingsService` + admin section to `ILoggingSettingsCommandService`.
**Not part of Stage 5 slice 1.**

---

## 6. Boundaries

`SiNet.App.Wpf` must **not**:

- Reference `SettingsManager`, `AppSettings`, `AppLogger`, `CentralLoggingSettings`
- Read/write `settings.json` directly
- Call Serilog APIs

Enforced by `SettingsStage5BoundaryTests.cs`.

---

## 7. Remaining work (post–Stage 5 slice 1)

| Item | Target |
| --- | --- |
| Native Settings window | `SettingsWindow` parity → App.Wpf Admin/Settings |
| Theme / layout on `IAppSettingsService` | Font, colors, floating windows |
| Bootstrap unification | Single local log directory; optional Serilog path reload |
| Extract `CentralLogging` bootstrap | `SiNet.Infrastructure.Logging` host extension |
| `ManagementSettingsWindow` parity | Full admin settings beyond logging |

No schema / migration changes in Stage 5.
