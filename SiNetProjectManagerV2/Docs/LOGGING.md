# SiNet Centralized Logging System

## Overview

All three SiNet apps share a single, **DB-driven** logging configuration so logs from
every machine and every user funnel into one well-known network share with a
consistent format and folder structure.

| App | Project | TFM | Default local level | Default central level |
|---|---|---|---|---|
| WPF Client | `SiNetProjectManagerV2` | `net10.0-windows` | Error (toggleable) | Error |
| ACC Service | `SiOffice.AccService` | `net10.0-windows` | Information | Warning |
| MasterPlan Sync | `MasterPlan.SyncEngine` | `net10.0` | Information | Warning |

Logging framework: **Serilog 4.2** (sinks: File 7.0 + Async 1.5 + Console 6.0).
Shared configuration code lives in `SiNetSQL\Services\Logging\CentralLogging.cs`
and is re-used by `MasterPlan.SyncEngine` via `<Compile Include Link="..."/>`
(no `ProjectReference` — keeps the slim `net10.0` TFM).

## Storage Layout

### Central share

```
\\si-win-2k19\AutoCAD Data\log\
    <AppName>\<MachineName>\<UserName>\<AppName>-yyyyMMdd.log
```

`<AppName>` is one of `Client`, `AccService`, `SyncEngine`. The three-level
sub-folder layout makes it trivial to grep a specific user/machine without
opening a single huge folder.

### Local fallback (per machine)

| App | Local path |
|---|---|
| Client | `%LocalAppData%\SiNetProjectManager\Logs\` |
| AccService | `%ProgramData%\SiOffice\AccService\logs\` |
| SyncEngine | `%ProgramData%\SiOffice\MasterPlanSync\logs\` |

Files roll daily and on a 10 MB size limit. The local sink keeps **14** days,
the central sink keeps **90** days (overridable in DB).

## Output Template

Every sink in every app uses the exact same template — keeps the central log
greppable across sources:

```
[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{App}] [{Machine}] [{User}] [P{ProcessId:D5}/T{ThreadId:D3}] [{Level:u4}] {Message}{NewLine}{Exception}
```

Example:

```
[2025-01-15 14:32:15.123] [Client] [WS-DANNY] [danny] [P12345/T001] [INFO] Logger initialized
[2025-01-15 14:32:17.789] [AccService] [SI-WIN-2K19] [SYSTEM] [P09872/T015] [WARN] ACC token refresh stale
```

## Configuration — Single Source of Truth: `SystemSettings` table

All knobs live in `dbo.SystemSettings` under the `Logging.*` prefix. The
WPF Admin UI (Management Settings) is the only place users edit them.

| Key | Type | Default | Description |
|---|---|---|---|
| `Logging.CentralLogPath` | UNC string | `\\si-win-2k19\AutoCAD Data\log` | Root of the central share. Empty = central sink disabled. |
| `Logging.LocalRetentionDays` | int | `14` | Local rolling-file retention. |
| `Logging.CentralRetentionDays` | int | `90` | Central rolling-file retention. |
| `Logging.Client.FileLevel` | level | `Error` | WPF client local file min level. |
| `Logging.Client.CentralLevel` | level | `Warning` | WPF client central file min level. |
| `Logging.AccService.FileLevel` | level | `Information` | AccService local file min level. |
| `Logging.AccService.CentralLevel` | level | `Warning` | AccService central file min level. |
| `Logging.SyncEngine.FileLevel` | level | `Information` | SyncEngine local file min level. |
| `Logging.SyncEngine.CentralLevel` | level | `Warning` | SyncEngine central file min level. |

Valid level values (case-insensitive): `Verbose`, `Debug`, `Information`,
`Warning`, `Error`, `Fatal`.

### How settings are loaded

Each app, at process start, calls
`CentralLoggingSettings.LoadFromDatabase(connectionString, app)` which:

1. Opens a **raw `SqlConnection`** (no EF, no DI yet — must work before the
   container is built).
2. Runs `SELECT SettingKey, SettingValue FROM dbo.SystemSettings WHERE SettingKey LIKE 'Logging.%'`
   with a 5-second `CommandTimeout`.
3. Maps the rows; missing rows fall back to the per-app code defaults
   (`CentralLoggingDefaults`).
4. **Any failure** (DB unreachable, parse error, share missing) is swallowed —
   the logger always boots, even if only the local sink is active.

### Optional dynamic level switch (WPF only)

The WPF client passes `AppLogger.FileLevelSwitch` (a `LoggingLevelSwitch`)
into `LoadFromDatabase`. The local sink is then controlled by that switch
instead of `LocalFileMinLevel`, so the existing in-app Settings toggle keeps
working at runtime without rebuilding the logger.

## Bootstrapping in Each App

### WPF Client — `App.xaml.cs` (static ctor)

```csharp
var connStr = CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase);
var loggingConfig = CentralLoggingSettings.LoadFromDatabase(
        connStr, SiNetApp.Client, enableConsole: false, AppLogger.FileLevelSwitch)
    with { LocalLogDirectory = _logDir };

Log.Logger = new LoggerConfiguration()
    .AddSiNetCentralLogging(loggingConfig)
    .CreateLogger();
```

### AccService — `Program.cs` (before `Host.CreateApplicationBuilder`)

The CredentialProvider bridge must be installed BEFORE the logger reads the
connection string from the vault.

```csharp
var connStr = CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase)
              ?? builder.Configuration.GetConnectionString("SiNetDatabase");

Log.Logger = new LoggerConfiguration()
    .AddSiNetCentralLogging(
        CentralLoggingSettings.LoadFromDatabase(connStr, SiNetApp.AccService, enableConsole: true))
    .CreateLogger();
```

### SyncEngine — `Program.cs`

Console + file. The shared library is pulled into the project via
`<Compile Include="..\..\SiNetSQL\SiNetSQL\Services\Logging\CentralLogging.cs" Link="..."/>`
plus `SystemSettingKeys.cs` — no `ProjectReference`.

```csharp
Log.Logger = new LoggerConfiguration()
    .AddSiNetCentralLogging(
        CentralLoggingSettings.LoadFromDatabase(connStr, SiNetApp.SyncEngine, enableConsole: true))
    .CreateLogger();

ILoggerFactory loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: true);
AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
```

## Build Notes

- **Always build via Visual Studio MSBuild**, not `dotnet build`. `SiNetSQL.csproj`
  has a COM reference (`IWshRuntimeLibrary`) that `dotnet` rejects with
  `MSB4803`.
- VS MSBuild path:
  `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- Solution: `SiNetProjectManager.sln`.

## Maintenance

- Default retention: 14 days local / 90 days central.
- The central share is keyed by `\<AppName>\<Machine>\<User>\` so a single user
  on a single machine can be wiped without touching others.
- The legacy `appsettings.json → Logging.CentralLogPath` is **deprecated** and
  ignored — left in place only as a hint for first-run when the DB is
  unreachable.

## Lifecycle Markers (guaranteed central-log entries)

To prove the central share is healthy even when nothing is going wrong, every
app emits **lifecycle markers at `Warning` level** — that level reaches the
central sink under all default per-app settings. If the central folder for an
app/machine/user stays empty, the share or its permissions are broken; it is
not "no events".

| App | Event | Source |
|---|---|---|
| WPF Client | `SiNetProjectManagerV2 opened — version … session …` | `App.OnStartup` |
| WPF Client | `SiNetProjectManagerV2 closing — exit code … session …` | `App.OnExit` |
| AccService | `SiOffice.AccService starting / started / stopping / stopped` | `Program.cs` lifetime hooks |
| SyncEngine | `MasterPlan.SyncEngine starting — args …` | `Program.cs` top-level |
| SyncEngine | `MasterPlan.SyncEngine stopped — exit code …` | `AppDomain.ProcessExit` |
| SyncEngine | `MasterPlan.SyncEngine DB update started / finished — mode {Mode}, duration {Duration}` | wraps each `--daily / --daily-db / --monthly` run |

## Troubleshooting

### Nothing in the central share
1. Verify the share `\\si-win-2k19\AutoCAD Data\log` is reachable as the
   running process's identity (LOCAL SYSTEM for AccService — make sure the
   service account has write rights).
2. Check the per-app local file — it always works even when the central share
   is down and will record write failures.
3. Make sure `Logging.<App>.CentralLevel` isn't set to a level higher than
   what your code emits.

### Levels not applying
1. Edit the value in **Management Settings → Logging** (not in
   `appsettings.json`).
2. Restart the app — levels are read once at process start.

### Want richer local logs only on one developer's machine
For the WPF client, lower the `AppLogger.FileLevelSwitch.MinimumLevel` at
runtime — the central sink is unaffected, the local sink follows the switch
immediately.
