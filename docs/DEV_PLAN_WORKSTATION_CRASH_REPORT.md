# DEV plan — Workstation crash report («דוח קריסות תחנה»)

> **Title:** Workstation crash report (DEV-010)
> **Date:** 05.08.2026
> **Updated:** 05.08.2026
> **Status:** Implementing
> **Scope:** In-app replacement for the ad-hoc PowerShell `Get-WinEvent` script used to investigate Civil 3D / acad.exe crashes and unstable workstations. Local machine only; export for offline AI analysis.

Related: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`SETTINGS.md`](./SETTINGS.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

---

## 1. Purpose

Several workstations crash repeatedly (Civil 3D / AutoCAD, and sometimes the machine itself). Today the only tool is a hand-run PowerShell script that reads `Application` events 1000/1001/1002 for the current day.

This feature turns that into a first-class, self-service screen:

1. Any signed-in user can produce a crash report for **their own** machine.
2. The report combines **application crashes** with **machine/hardware events**, and marks the correlation between them.
3. The user states **why** the report was produced — that human context is what makes an AI analysis useful instead of guesswork.
4. Output is a clean CSV (data) plus a Markdown analysis file (self-contained, ready to hand to an AI), optionally saved to a shared folder per machine so several workstations can be compared.

**Non-goal:** the app does not diagnose. It reports facts and flags; interpretation stays with a human or an external AI.

---

## 2. Event sources

### 2.1 Application log

| Event | Provider | Meaning |
| --- | --- | --- |
| 1000 | Application Error | Process crash; carries `AppName`, `AppVersion`, `ModuleName`, `ExceptionCode` |
| 1001 | Windows Error Reporting | Supporting WER bucket / report id |
| 1002 | Application Hang | Process stopped responding |
| 1026 | .NET Runtime | Managed crash (typically an add-in) |

Filtered by the app-name list (see §4).

### 2.2 System log (machine health)

| Event | Provider | Meaning |
| --- | --- | --- |
| 41 | Kernel-Power | Rebooted without a clean shutdown (power, heat, PSU, bugcheck) |
| 6008 | EventLog | Previous shutdown was unexpected |
| 1001 | BugCheck | Blue screen, with the stop code |
| 17 / 18 / 19 | WHEA-Logger | Corrected/uncorrected hardware error (CPU, PCIe, memory) |
| 7 / 11 / 153 | disk | Bad block, controller error, IO retry |
| 55 | Ntfs | File-system corruption |

These are **not** filtered by app name — they describe the machine.

---

## 3. One report, not two

**Decision:** a single report with a focus selector and per-event severity — not separate "AutoCAD crash" and "machine crash" reports.

The diagnostic value lives in the intersection. If Civil 3D crashed at 14:32 and a `WHEA-Logger` or disk error appeared a minute earlier, the cause is hardware, not software — and two separate reports would hide exactly that link.

### 3.1 Focus selector — `CrashReportScope`

| Value | Included |
| --- | --- |
| `Both` (default) | Application + System, with correlation |
| `ApplicationOnly` | Civil 3D / acad crashes only |
| `MachineOnly` | Shutdowns, bugchecks, hardware, disk |

### 3.2 Severity — `CrashSeverity`

| Level | Events | Meaning |
| --- | --- | --- |
| `Critical` | BugCheck 1001, Kernel-Power 41, EventLog 6008, WHEA 17/18/19, disk 7/11/153, Ntfs 55 | The machine itself failed, or hardware reported an error |
| `AppCrash` | Application Error 1000, Hang 1002, .NET Runtime 1026 | The application died; the OS kept running |
| `Supporting` | WER 1001 and context events | Supporting detail, not an incident on its own |

### 3.3 Factual flags (no diagnosis)

The summary shows only measurable facts: `HasBugCheck`, `HasHardwareEvents`, `HasUnexpectedShutdown`, `CrashesPerDay`, correlated-event count, top faulting modules, top exception codes.

**Correlation rule:** an `AppCrash` within **5 minutes** of a `Critical` event is tagged `CorrelatedWith`.

---

## 4. Parameters

### 4.1 User context (required before generating)

| Field | Required | Notes |
| --- | --- | --- |
| Reason category | Yes | Civil 3D repeat crash · unexpected shutdown/restart · blue screen · freeze/slowness · crash during a specific action · other |
| Free-text description | Yes | What happened, when it started, what you were doing, whether it repeats (max ~1000 chars) |
| Last occurrence | No | Date/time that helps the AI focus on the right window |

Context is written at the top of the Markdown file next to the machine profile, and the category slug goes into the file name. The CSV stays purely tabular.

### 4.2 Collection parameters

| Parameter | Default | Source |
| --- | --- | --- |
| Lookback days | 14 | `Diagnostics.CrashLookbackDays` |
| App name filters | `acad.exe,civil 3d,aecc,revit.exe` | `Diagnostics.CrashAppFilters` |
| Scope | `Both` | User |
| Max events | 2000 | User (guard against huge files) |

### 4.3 Captured per event

Time, type, event id, provider, severity, `AppName`, `AppVersion`, `ModuleName`, `ModuleVersion`, `ExceptionCode`, `FaultOffset`, `AppPath`, `ModulePath`, report id, correlation tag, and a truncated message (~1500 chars).

### 4.4 Machine profile

Machine name, user, Windows version/build, CPU, RAM, free space on the system drive, GPU with driver version and date, detected Civil 3D / AutoCAD versions, uptime, and last Windows update time.

This block is the single biggest improvement over the script: without it an AI cannot tell a GPU-driver crash from a RAM problem.

---

## 5. Architecture

```
SiNet.Application/Diagnostics/          ports, DTOs, builder, formatter (pure)
SiNet.Infrastructure.Diagnostics/       EventLog reader, machine profile, report store (net10.0-windows)
SiNet.App.Wpf/Admin/Diagnostics/        view, view model, window
```

| Piece | Type |
| --- | --- |
| `IWorkstationEventLogReader` | Port — reads Application/System events for a query |
| `IMachineProfileProvider` | Port — hardware/OS profile |
| `ICrashReportStore` | Port — save CSV/Markdown, apply retention, open folder |
| `WorkstationCrashReportBuilder` | Pure — severity, correlation, aggregation |
| `WorkstationCrashReportFormatter` | Pure — `ToCsv()` / `ToMarkdown()` |

The infrastructure module targets `net10.0-windows` and references the `System.Diagnostics.EventLog` and `System.Management` packages, mirroring `SiNet.Infrastructure.Secrets`. `SiNet.Infrastructure.Logging` cannot host it — it targets plain `net10.0`.

**Registration:** `AddSiNetWorkstationDiagnostics()` is called by each host composition root — `StandaloneHostServiceCollectionExtensions` (production) and the V2 hybrid graph — **not** by `SiNet.App.Composition`. Same constraint as the Credential Vault: Composition is platform-neutral `net10.0`, and moving a Windows-only module there would force every consumer onto a Windows TFM.

---

## 6. Output and storage

Files are written under `{share}\{MachineName}\`:

```
{MachineName}_{yyyy-MM-dd_HHmm}_{category}_crashes.csv
{MachineName}_{yyyy-MM-dd_HHmm}_{category}_analysis.md
```

The share resolves from `Diagnostics.CrashReportSharePath`; when empty it falls back to `{Logging.CentralLogPath}\CrashReports`. Writing is verified with the same probe behavior already used for the central log path.

The user can also export either file anywhere via a save dialog.

### 6.1 Retention

After a successful save to the share, old reports are cleaned **inside that machine's folder only**:

- Delete `*_crashes.csv` / `*_analysis.md` older than `Diagnostics.CrashReportRetentionDays`.
- Always keep the newest 5 reports, even when older than the threshold.
- Best-effort: a delete failure is logged and surfaced as a status warning; it never fails the save.
- Never touch other machines' folders, and never delete files that do not match the naming pattern.

---

## 7. Access

The menu item «דוח קריסות תחנה» lives in the **מנהלה** group and requires only a signed-in user — no new feature code. A user whose machine is crashing must be able to produce a report without admin rights, exactly like «מצב מערכת» and «הגדרות אישיות» in the same group.

Editing the `Diagnostics.*` keys themselves stays in «הגדרות מערכת» under the existing `System.Settings.Write` feature, so a regular user cannot change the share path or retention for everyone.

Reading the `Application` and `System` logs works for standard users. The `Security` log is never read.

---

## 8. Out of scope

- Remote inspection of other machines. To cover another workstation, run the app there and save to the share.
- Civil 3D's own crash artifacts (`*.dmp`, CER packages).
- SiNet's own Serilog files and `AppErrorReporter` entries — this report is about Civil 3D and the machine, and it stays consistent with the "no log scraping" direction in [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) §3.3.
- An in-app «נתח ב-AI» button. The existing `AddSiNetAi()` path is Ollama-only and inspection-specific; a generic AI port would be a separate slice.
- A «מצב תחנה» row in «מצב מערכת» via `ISubsystemStatusContributor` — possible later.
