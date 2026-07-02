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
Host bootstrap (SiNetProjectManagerV2 App.xaml.cs)  (central + local sinks, unchanged)
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
`AppLogger.Configure` / file sink level. That remains legacy host behavior until a native settings
surface migrates it behind an Application port.

**Global / central logging:** DB keys `Logging.*` (`CentralLoggingSettings`) remain in SiNetSQL for now.
Extracting bootstrap into `SiNet.Infrastructure.Logging` is a later slice — Stage 4 only wires the
**consumption port** for New System modules.

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

Enforced by `NewSystemLoggingBoundaryTests.cs` + `NewSystemBoundaryTests.cs`.

---

## 7. Remaining work (post–Stage 4)

| Item | Target |
| --- | --- |
| Extract `CentralLogging` bootstrap | `SiNet.Infrastructure.Logging` (optional host extension) |
| Migrate `AppLogger.*` call sites | Gradual; or shim delegating to `IAppLogger` |
| Native settings surface | `IAppSettingsService` for logging toggle / directory |
| `IAppLogger.Debug` / structured context | Extend port when a consumer needs parity with `AppLogger.Debug` |

No schema / migration changes in Stage 4.

---

## 8. Related docs

- [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md) — no legacy logger in App.Wpf
- [`APP_SHELL.md`](./APP_SHELL.md) §11 — settings mechanisms (logging toggle today in legacy `SettingsWindow`)
- [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) — Logging domain / D2 composition gate
