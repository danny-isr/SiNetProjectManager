# Logging — New System target (Stage 4)

> **Status:** Stage 4 implemented (2026-07-03) — Application port + Serilog adapter for New System.
> **Legacy reference:** `SiNetProjectManagerV2/Docs/LOGGING.md` (ops / central logging / DB keys).

---

## 1. Goal

Give the New System stack a **single Application logging port** (`IAppLogger`) with a **Serilog-backed
adapter** that reuses the host's existing Serilog pipeline — not a second logger, not a legacy static
`AppLogger` dependency from `SiNet.App.Wpf`.

```text
SiNet.Application.Abstractions.Logging.IAppLogger   (port)
        ↓
SiNet.Infrastructure.Logging.SerilogAppLogger       (adapter → Serilog Log.Logger)
        ↓
Host bootstrap:
  - Legacy / V2Hybrid: SiNetProjectManagerV2 App.xaml.cs (central + local sinks)
  - Standalone New System: StandaloneHostLoggingBootstrap (SiNet.Infrastructure.Logging)
    — Serilog stays out of SiNet.App.Wpf source; WpfLoggingRuntimeApplier calls the bootstrap
```

---

## 2. What exists today

| Layer | Legacy (production host) | New System (Stage 4) |
| --- | --- | --- |
| Bootstrap | `App.xaml.cs` static ctor → Serilog `Log.Logger`, `CentralLogging`, `AppLogger.FileLevelSwitch` | **Unchanged** — host still owns bootstrap |
| Static facade | `SiNetSQL.Services.AppLogger` | **Not used** by `SiNet.App.Wpf` or New System modules |
| DI port | — | `IAppLogger` |
| Adapter (scaffold) | — | `ConsoleAppLogger` via `AddSiNetLogging()` (scaffold / tests only) |
| Adapter (production) | — | `SerilogAppLogger` via `AddSiNetSerilogLogging()` |
| MEL bridge | `AddLogging` + `AddSerilog(Log.Logger)` | Same `Log.Logger` — `SerilogAppLogger` writes to it |

**Per-user toggle:** `AppSettings.LoggingEnabled` + `LogDirectory` (`SettingsWindow`) still control
`AppLogger.Configure` / file sink level via host adapter `ILoggingRuntimeApplier` (Stage 5). Native
Settings UI is deferred; ports are in `SiNet.Application.Settings` — see [`SETTINGS.md`](./SETTINGS.md).

**Global / central logging:** DB keys `Logging.*` are read/written via `ILoggingSettingsQueryService` /
`ILoggingSettingsCommandService` (Stage 5). The shared Serilog sink layout
(`CentralLoggingSettings` / `AddSiNetCentralLogging`) lives in
`SiNet.Infrastructure.Logging` (B1 AccService decoupling). Hosts (V2, AccService,
MasterPlan.SyncEngine) call that module at bootstrap via ProjectReference.

### 2.1 Central sink per host — actual state (updated 13.08.2026)

| Process | Calls `AddSiNetCentralLogging` | Local sink | Central (network) sink |
| --- | --- | --- | --- |
| `SiNetProjectManagerV2` (Legacy) | Yes — `App.xaml.cs` | Level via `AppLogger.FileLevelSwitch` | Yes, own `CentralMinLevel` |
| `SiOffice.AccService` | Yes — `Program.cs` | Yes | Yes |
| `MasterPlan.SyncEngine` | Yes — `Program.cs` | Yes | Yes |
| **`SiNet.App.Wpf` (production desktop)** | **Yes** — phase 2 `StandaloneHostLoggingBootstrap.ConfigureCentral` after vault SQL | Local file + level switch (`ApplyUserLogging`) | Yes — `SiNetApp.Client` → `Client\<machine>\<user>\` |

`SiNet.App.Wpf` boots in two phases ([§9](#9-standalone-host-central-logging--target-state)): local-only until the vault yields a connection string, then local + central. `LoggingEnabled=false` moves **only** the local switch (typically to Fatal); it must **not** silence the central sink. Default `Logging.Client.CentralLevel` remains **Warning**.

Startup lines such as `Opening NewShell...` / `Standalone New System ready.` are **Information**, so a healthy session may create **no** central file. That is intended. Material **failures** must still be Warning/Error — catalogue: [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md). (The 2026-07-29 note that standalone had **no** central sink is **superseded**.)

---

## 3. Application port

Location: `src/SiNet.Application/Abstractions/Logging/IAppLogger.cs`

```csharp
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
```

Rules:

- **No** Serilog, **no** `SiNetSQL.Services.AppLogger`, **no** `Microsoft.Extensions.Logging` in
  `SiNet.App.Wpf` or Application layers.
- Infrastructure modules (e.g. `SiNet.Infrastructure.Google`) inject `IAppLogger` only.

---

## 4. Infrastructure adapters

| Adapter | Registration | When |
| --- | --- | --- |
| `ConsoleAppLogger` | `AddSiNetLogging()` | Scaffold host (`AddSiNet()`), unit tests without Serilog |
| `SerilogAppLogger` | `AddSiNetSerilogLogging()` | Production New System graph — delegates to `Log.Logger` |

`SerilogAppLogger` does **not** create sinks or change levels. It forwards `Info`/`Warn`/`Error` to the
Serilog logger the host already configured. One pipeline, two entry styles (static `AppLogger` legacy +
DI `IAppLogger` New System).

---

## 5. DI wiring (New System)

`NewSystemServiceCollectionExtensions.AddSiNetNewSystemGraph()` calls `AddSiNetSerilogLogging()` so
every New System module resolving `IAppLogger` shares the host Serilog pipeline.

**Do not** call `AddSiNetLogging()` (ConsoleAppLogger) in the production host — that would register a
different adapter for the same port.

---

## 6. Boundaries

`SiNet.App.Wpf` must **not**:

- Reference `SiNetSQL.Services.AppLogger`
- Reference Serilog packages directly
- Call `Log.Logger` or `ILogger<T>` from views/viewmodels

Enforced by `NewSystemLoggingBoundaryTests.cs`, `ErrorHandlingSafetyNetTests.cs`, and
`NewSystemBoundaryTests.cs`.

`AppErrorReporter` records unexpected WPF-layer exceptions without referencing Serilog or legacy
`AppLogger`. The production host may subscribe to forward reports to `IAppLogger`.

---

## 7. Remaining work (post–Stage 4)

| Item | Target |
| --- | --- |
| Standalone host central sink + level switch | **Done (2026-07-29)** — see [§9](#9-standalone-host-central-logging--target-state) |
| Extract `CentralLogging` bootstrap | **Done (B1)** — `SiNet.Infrastructure.Logging`; SyncEngine Shared copy still pending |
| Migrate `AppLogger.*` call sites | Gradual; or shim delegating to `IAppLogger` |
| Native settings surface | `IAppSettingsService` for logging toggle / directory |
| `IAppLogger.Debug` / structured context | Extend port when a consumer needs parity with `AppLogger.Debug` |

No schema / migration changes in Stage 4.

---

## 8. Related docs

- [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) — pilot live-tail commands, central vs local levels, what to grep for
- [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md) — **Documentation-only:** which operation failures must reach Llog at Warning+ (ACC upload, MoveToProject, Gmail filing); Trace is not an ops channel
- [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) — PROD/DEV log share and target `Environment` enricher
- [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md) — no legacy logger in App.Wpf
- [`APP_SHELL.md`](./APP_SHELL.md) §11 — settings mechanisms (logging toggle today in legacy `SettingsWindow`)
- [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) — Logging domain / D2 composition gate
- [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) — pilot envelope (diagnosability)

---

## 9. Standalone host central logging — target state

> **Status:** Implemented (2026-07-29).
> **Applies to:** `SiNet.App.Wpf` (`SiNetHostMode.StandaloneNew`).
> **Guards:** `StandaloneHostLoggingBootstrapTests` (`src/SiNet.App.Wpf.Tests/Logging`).
> **Verified live:** `CentralEnabled=true`, probe write into
> `\\si-win-2k19\AutoCAD Data\log\Client\<machine>\<user>` succeeded.

### 9.1 Principles

1. **The central (network) sink is not user-controllable.** It is an operations channel. A per-user
   toggle must never be able to silence it. Only the DB key `Logging.Client.CentralLevel` governs it.
2. **The local sink is user-controllable.** `LoggingEnabled` / `LogDirectory` affect the local file only.
3. **The toggle moves a level switch — it does not rebuild the pipeline.** `ApplyUserLogging` only
   changes the local sink switch. Rebuilding `Log.Logger` would drop the central sink; that defect
   was fixed (2026-07-29).
4. **Serilog stays out of `SiNet.App.Wpf`.** Enforced by `NewSystemLoggingBoundaryTests`
   (`App_Wpf_csproj_does_not_reference_serilog_packages`). All wiring lives in
   `SiNet.Infrastructure.Logging`; the host calls named bootstrap methods only.

### 9.2 Two-phase bootstrap

The central config is DB-driven, but the SQL connection string only becomes available after the
vault gate. So the host logger boots twice, by design:

| Phase | When | Sinks |
| --- | --- | --- |
| **1 — local fallback** | `App.xaml.cs` `OnStartup`, before the vault gate | Local file only, Debug — so vault/DB failures are still recorded |
| **2 — full pipeline** | Right after the SQL connection string is resolved, before composition | Local (level switch) + central network, per `CentralLoggingSettings.LoadFromDatabase` |

```text
ConfigureDefault()                       → phase 1 (unchanged)
ConfigureCentral(sqlConnectionString)    → phase 2 (new)
ApplyUserLogging(settings)               → moves the local level switch only
```

### 9.3 Application identity

Standalone reuses **`SiNetApp.Client`**, not a new enum value:

- `Logging.Client.FileLevel` / `Logging.Client.CentralLevel` rows already exist and are managed by
  the Admin UI. A new `SiNetApp` member would fall through `LoadFromDatabase`'s `_ => string.Empty`
  branch and become silently non-configurable.
- `SiNet.App.Wpf` replaces `SiNetProjectManagerV2` as *the* client, so one identity is correct.
- Both sinks already use `shared: true`, so V2 and App.Wpf may write concurrently during the
  transition.
- To keep the two hosts distinguishable, phase 2 adds `Enrich.WithProperty("Host", "SiNet.App.Wpf")`
  on top of the shared `App` / `Machine` / `User` / `ProcessId` enrichers.

### 9.4 Operational side effects

| Before | After |
| --- | --- |
| Local file `SiNet-Standalone-yyyyMMdd.log` | `Client-yyyyMMdd.log` — `AddSiNetCentralLogging` derives the prefix from `SiNetApp` |
| Local directory `%LOCALAPPDATA%\SiNet\Logs` | Unchanged (passed explicitly as `LocalLogDirectory`) |
| No central file | `<CentralLogPath>\Client\<machine>\<user>\Client-yyyyMMdd.log` |
| Plain message lines | Shared output template with `App` / `Machine` / `User` / `ProcessId` / `ThreadId` |

Old `SiNet-Standalone-*.log` files are left in place; they are not migrated or deleted. The central
file only materialises on the first Warning/Error, because `Logging.Client.CentralLevel` is
`Warning` by default — an Information-only session legitimately produces no central file.
Material failures (ACC upload, MoveToProject, mailbox File/Unfile, Ensure Inbox) must still
be logged at Warning/Error so they appear on that share — see [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md).

### 9.5 Effect on the pilot

Once implemented, the default `LoggingEnabled = false` stops being a diagnosability problem: the
local file stays quiet, while the central share keeps receiving Warning/Error from every pilot user.
The default itself does **not** need to change.

### 9.6 Out of scope

- Changing `UserAppSettingsDefaults.LoggingEnabled`
- Changing the central path default or retention
- Touching V2 / AccService / SyncEngine bootstrap
- The duplicated `LogDirectory` / `logDirectory` keys written by `JsonAppSettingsService.WriteDto`
  (tracked separately; harmless to `System.Text.Json`, breaks case-insensitive readers)
