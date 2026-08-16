# DEV-028 — הנחיית פיתוח: בדיקת כתיבת לוג בהפעלה + שורה ב«מצב מערכת»

> **Title:** Startup must prove it can write the Client log (local + Llog); failure is a System Status note  
> **Date:** 16.08.2026  
> **Updated:** 16.08.2026 (Slice E: also prove central min level is Warning; הערה if not)  
> **Status:** Implementing  
> **Scope:** Product/engineering directive for the **`development`** branch. `SiNet.App.Wpf` must treat “can I write a log that I opened?” as a **startup check**, not as a diagnostic flag. If central (Llog) write cannot be proven, show a Hebrew note in «מצב מערכת». **Also** prove the applied Client central min level is **Warning**; if it is not, show that as a **הערה** (existing `GuidanceHe`) — not as “marker missing in yesterday’s file”. No new logger, no second health bus, no EF/schema.  
> **Branch:** Implement on `development`; ship to users via the normal `release` + `publish-all.ps1` desktop channel.  
> **Priority:** P0 (ops cannot see any workstation in Llog after 1.0.32)

Related: [`LOGGING.md`](./LOGGING.md) §9 / §9.4.1, [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md), [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`OPS_LLOG_REVIEW.md`](./OPS_LLOG_REVIEW.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md), [`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md) (DEV-027 — Fast/Deep; do not conflate with this row), [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) (DEV-002 — token Critical popup; **not** this slice).

---

## 1. Why (evidence 16.08.2026)

PROD published **SiNet.App.Wpf 1.0.32** with Warning heartbeat lines (`[STARTUP] Client process alive` / sinks / `Client session ready`). Operator installed the MSIX on the pilot stations.

| What we expected | What actually happened |
| --- | --- |
| Llog `Client\<machine>\<user>\Client-20260816.log` with Warning heartbeat | **No Client file grew or was created today** (delta New=0 Grown=0). Rachel / Omer / Masha still have **no** central file at all. Lilach last 17.06; Sarita 13.08; Danny central last 10.08 |
| `CentralEnabled=true` means Llog received bytes | Danny local `%LOCALAPPDATA%\SiNet\Logs\Client-20260816.log` at 13:29 has `[WARN] [STARTUP] Client process alive` and `CentralEnabled=true` pointing at `\\si-win-2k19\AutoCAD Data\log\Client\DESKTOP-L6KUN53\Danny\Client-.log` — **that UNC folder has no 16.08 file** |
| Folder probe = writable | `TryProbeCentralDirectory` writes a 1-byte `.sinet-logprobe-*.tmp` and deletes it. That can succeed while Serilog’s rolling `Client-yyyyMMdd.log` never appears on the share the operator reads |

Operator lock: **claiming the sink is connected is not enough.** Startup must **prove a log line exists on disk** (local, and central when configured). If central cannot be proven, the user must see it in «מצב מערכת» — not only in a local file the operator never opens.

1.0.32 did add Warning lines **locally**. It did **not** give production an Llog proof of life. That is this ticket, not a re-publish of the same code.

**Second lock (16.08 evening, Slice E):** even with read-back, the check lied when `Logging.Client.CentralLevel` was **Error**. Warning heartbeats were filtered; `ResolveTodayLogPath` then opened the newest *any* `Client-*.log` (Danny: **20260810**) and reported `marker missing in …\Client-20260810.log`. Ops restored `SiData.dbo.SystemSettings` `Logging.Client.CentralLevel` = **Warning** (row `LastUpdated` 16.08.2026 13:26 UTC). After restart, Danny’s `Client-20260816.log` on Llog contains the pid marker. **Code must still detect a non-Warning central min** so this cannot hide again. AccService / SyncEngine levels stay Warning — do not touch them.

---

## 2. Existing mechanisms (reuse — do not invent parallel stacks)

| Mechanism | Where | Reuse |
| --- | --- | --- |
| Two-phase Serilog | `StandaloneHostLoggingBootstrap.ConfigureDefault` then `ConfigureCentral` | Keep. Phase 1 = local before vault; phase 2 = central after SQL |
| Heartbeat messages | `App.OnStartup` + `LogSinkDiagnostics` + `Client session ready` ([`LOGGING.md`](./LOGGING.md) §9.4.1) | **Keep the three Warning texts.** Add **verify** after they are written. Heartbeat stays Warning — do **not** lower it to Error |
| Applied central min | `CentralLoggingConfig.CentralMinLevel` after `ConfigureCentral` (`Logging.Client.CentralLevel`) | **Slice E:** expose last applied level from `CentralLoggingBuilder`. Expected **Warning**. If not, `GuidanceHe` הערה |
| Central folder probe | `CentralLoggingBuilder.TryProbeCentralDirectory` — create dir + 1-byte tmp + delete | Necessary, **not sufficient**. Do not treat `CentralSinkEnabled` as the startup check |
| `CentralSinkEnabled` / `CentralSinkBootstrapError` | `CentralLogging.cs` | Keep as bootstrap diagnostics. UI must use the **read-back** result, not this flag alone |
| Central File sink | `WriteTo.Async` → `File` (`shared: true`, daily roll, path `{App}-.log`) | First hypothesis for “probe ok, file missing”: Async buffer / UNC / MSIX. Investigate; do not add a second File sink |
| Local file | `%LOCALAPPDATA%\SiNet\Logs\Client-yyyyMMdd.log` (`UserAppSettingsDefaults` / `LoggingEnabled` switch) | Heartbeat is Warning so it still writes locally during phase 1 even if the user later turns local off |
| Immediate window | `StartupSplashWindow` (`SetStatus`) during `App.RunStartupAsync` | **The** place to show the check while starting (“בודק כתיבת לוג…”) |
| System Status | `IRuntimeSubsystemStatusService` + `ISubsystemStatusContributor`; first refresh ~3s; footer + `SystemStatusWindow` | **The** persistent note. New contributor, same bus ([`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md) §2) |
| Guidance | `SystemStatusGuidanceCatalog.Resolve` | Add key `logging-central` |
| File-server row | `FileServerStatusContributor` (`file-server`) — Directory.Exists on a project UNC | Different share. Do **not** overload it as the Llog check |
| Llog sweep | `.cursor/skills/llog-review` class `client-session-alive` | Acceptance on PROD: that class appears after a start |

Do **not** add a second Serilog pipeline, Seq, Event Log heartbeat, or `System.Diagnostics.Trace` “just in case”.

---

## 3. Target state (locked)

### 3.1 Startup check (mandatory)

After phase 2 (`ConfigureCentral`) and the Warning heartbeat lines, still on the splash, the host runs **one** check (off the UI thread, timeout bounded, e.g. 5s):

1. Emit a **unique** Warning marker that includes process id, e.g.  
   `[STARTUP] Client process alive pid={pid}`  
   (same family as §9.4.1; do not add V2 session-GUID flood).
2. **Flush** the Serilog pipeline so `WriteTo.Async` cannot leave the line in memory (`Log.CloseAndFlush` is too heavy if it tears down the logger — flush the sinks / recreate if that is what the existing bootstrap already does on rebuild; do not leave the marker only in the Async queue).
3. **Read back** the dated files **for today only**:
   - Local: `%LOCALAPPDATA%\SiNet\Logs\Client-yyyyMMdd.log` (or the configured `LogDirectory`)
   - Central: `{Logging.CentralLogPath}\Client\{Machine}\{User}\Client-yyyyMMdd.log`
   - If today’s file does not exist, that **today** path is the failure. **Do not** fall back to the newest `Client-*.log` in the folder (`StartupLogWriteVerifier.ResolveTodayLogPath` currently does — that is Slice E).
4. Success = the unique marker is **in the file bytes** the process just read from that path.
5. **Slice E:** also read the **applied** Client `CentralMinLevel`. Expected **Warning**. If it is Error/Fatal/Information/Debug/Verbose, attach the הערה in §3.3 — even when write-back later succeeds.

Folder tmp probe remaining green while step 4 fails = **failure** (this is the 16.08 Danny case). Central min Error while heartbeat is Warning = the same user-visible miss, with a **different** root cause — must not look like “marker missing in last week’s file”.

### 3.2 Splash («חלון מיידי»)

`StartupSplashWindow.SetStatus` must include this check in the existing startup sequence (alongside vault / schema / authorize). Example copy (lock meaning, not exact punctuation):

- In progress: `בודק כתיבת לוג…`
- Local+central OK: `הלוג נכתב (מקומי + מרכזי)`
- Local OK, central fail: `הלוג המרכזי לא נכתב — ראה מצב מערכת`
- Local fail: `לא ניתן לכתוב לוג מקומי — ראה מצב מערכת`

The app **does not abort** startup solely because central write failed (users must still work). Local+central failure is still **not** a silent continue: splash + System Status must show it.

### 3.3 «מצב מערכת» row

New `ISubsystemStatusContributor` registered in `AddSiNetSystemHealthContributors` (standalone host — same as other rows).

| Field | Lock |
| --- | --- |
| `Key` | `logging-central` |
| `DisplayNameHe` | `לוג מרכזי (Llog)` |
| Idle | Marker found in today’s central `Client-yyyyMMdd.log` |
| Degraded | Local wrote; central file missing / marker absent / probe-only success / SelfLog error |
| Stopped / Critical | Local file also missing after phase 1 (cannot diagnose this station from disk) |
| `SummaryHe` | Short: path attempted + “נכתב” / “לא נמצא הקובץ של היום” / exception type (no stack dump in the row). **Never** name a previous day’s `Client-yyyyMMdd.log` as the missing-marker file |
| `GuidanceHe` | Catalog UNC text when write failed and level **is** Warning. **Slice E:** if applied central min **is not Warning**, set `GuidanceHe` on the row (existing secondary line under «פירוט» — `SystemStatusView` binds `Guidance` / `HasGuidance`). This **is** the הערה. Do not add a column, a second contributor, or a MessageBox |

**הערה copy (meaning-locked, `{level}` = applied Serilog level name in Hebrew or English enum as already used in Admin):**

`הערה: רמת הלוג המרכזי היא {level} — נדרש Warning. שורות Warning (כולל דופק הפעלה) לא יגיעו ל-Llog.`

| Applied central min | Today’s central marker | Row state | What the user sees |
| --- | --- | --- | --- |
| Warning | Present | Idle | Summary “נכתב”; no הערה |
| Warning | Missing | Degraded | Summary = today’s file missing / marker absent; catalog UNC guidance |
| Error / Fatal (or anything other than Warning) | Missing (filtered) | Degraded | Summary = today’s file missing or filtered; **הערה** with the actual level |
| Not Warning | Present (should not happen for Error) | Idle | Summary “נכתב”; **still** the הערה — policy mismatch |

`SystemStatusGuidanceCatalog.Resolve` already prefers a non-empty `existingGuidanceHe`. Set the הערה on the contributor so the catalog does not replace it. For Idle + level mismatch, the catalog currently skips `logging-central` (Idle is not Degraded) — the contributor **must** set `GuidanceHe` itself.

Footer already goes non-green when any contributor is Degraded — reuse that. Do **not** add a second indicator. An Idle row with only the הערה does **not** turn the footer red (policy note, not write failure). That is intended.

Deep vs Fast (DEV-027): this row is **Fast** (file read of today’s log). Do not hit Autodesk/Gmail.

### 3.4 What Llog must contain after a good start

Same three Warning families as §9.4.1, **and** they must be visible on the UNC from the **ops workstation** (not only from inside the app process). Class `client-session-alive` on the next Llog sweep.

### 3.5 Fail-open vs fail-closed

| Event | App continues? | User sees |
| --- | --- | --- |
| Central write not proven | **Yes** | Splash line + `logging-central` Degraded |
| Local write not proven | **Yes** (unless existing Fatal startup path already fires) | Splash line + row Critical |
| Vault / SQL / authorize fail | Unchanged existing shutdown | Unchanged |

No extra MessageBox for this slice (DEV-002 owns the token popup). The note lives in splash + «מצב מערכת».

---

## 4. Implementation slices (for the DEV agent)

| Slice | Work | Done when |
| --- | --- | --- |
| **A** | Verify helper in `SiNet.Infrastructure.Logging` (flush + read dated file for marker). Unit tests with a **temp directory** as `CentralLogPath` / local dir — not the production UNC | Tests fail if Warning was logged but file lacks the marker (cover Async: test must still see bytes after verify) |
| **B** | Call verify from `App.RunStartupAsync` after `ConfigureCentral` + heartbeat; `StartupSplashWindow.SetStatus` copy §3.2 | Splash shows the check on every start |
| **C** | `LoggingCentralStatusContributor` + `SystemStatusGuidanceCatalog` key `logging-central` + register in `SystemHealthContributorsExtensions` | «מצב מערכת» row; footer reflects Degraded |
| **D** | Hypothesis pass: central `WriteTo.Async` vs sync `File` on UNC; MSIX virtualization vs real share | Document which fix made the **temp** test and a **real UNC** (DEV machine) both pass. Do not ship on temp-only green |
| **E** | Central min must be **Warning**; הערה if not; **no** fallback to an old dated file | See §3.1 step 5 and §3.3. Tests in `StartupLogWriteVerifierTests` |

Slice E work (reuse — do not invent a parallel check):

1. Store last `CentralLoggingConfig.CentralMinLevel` on `CentralLoggingBuilder` when `AddSiNetCentralLogging` runs (same pattern as `CentralSinkEnabled`). Verifier reads that. Do **not** add a second SQL round-trip only for this row.
2. `ResolveTodayLogPath`: if `Client-yyyyMMdd.log` for **today** is absent, return that dated path (missing). Delete the `EnumerateFiles` “newest any Client-*.log” fallback.
3. `StartupLogWriteVerificationResult`: add applied central level + `bool CentralLevelIsWarning` (or equivalent). Contributor sets `GuidanceHe` from §3.3 when false.
4. Tests (temp dirs, not prod UNC):
   - `CentralMinLevel = Error` → central verify fails (marker not in today’s file) **and** the result/contributor exposes the הערה (level is Error, required Warning).
   - Folder contains only `Client-20260810.log`; today is 16.08 → detail/path is **today’s** missing `Client-yyyyMMdd.log`, not the 10.08 file.
   - `CentralMinLevel = Warning` + marker in today’s file → Idle, no הערה.

No `Add-Migration`. No AccService / SyncEngine bootstrap change unless a shared `CentralLogging` flush API is required (prefer that over duplicating File sinks). **Do not** change `Logging.AccService.CentralLevel` / `Logging.SyncEngine.CentralLevel`. **Do not** lower the heartbeat to Error.

---

## 5. Risk and complexity (before DEV writes code)

| Area | Assessment |
| --- | --- |
| **Complexity** | Low–medium. One helper, one splash call, one contributor, catalog line, tests. Possible one-line sink change (drop Async on **central** only) |
| **Effort** | About one focused DEV day + PROD verify (one station start → Llog delta) |
| **Blast radius** | `AddSiNetCentralLogging` is shared with AccService / SyncEngine. **Do not** change their levels. If flushing/Async is changed, keep it **Client / standalone** or prove other hosts still write |
| **UI thread** | Verify and contributor I/O must be `async` / `Task.Run` with timeout — same rule as `FileServerStatusContributor` |
| **False green** | In-process `File.ReadAllText` on a virtualized MSIX path can succeed while ops UNC is empty. Slice D + PROD Llog is the real gate |
| **False “marker missing in old file”** | `ResolveTodayLogPath` newest-file fallback. Slice E removes it |
| **False “share broken” when level is Error** | Filtered Warning looks like write failure. Slice E הערה names the level |
| **DB/schema** | None. Ops already set PROD `Logging.Client.CentralLevel` = Warning (16.08). DEV DB should match policy; do not leave DEV on Error “to keep Llog quiet” |
| **Breaking** | Users keep working if Llog is down. Operators finally see a red row |

---

## 6. Out of Scope

- Lowering `Logging.Client.CentralLevel` to Information
- Lowering the startup heartbeat from Warning to Error (to “match” a quiet central level)
- Changing AccService / SyncEngine central levels
- A new System Status column or a second `logging-*` contributor for the level note
- V2 `SiNetProjectManagerV2 opened/closing` per-GUID lines
- In-app log viewer
- DEV-002 token MessageBox
- DEV-027 secrets import / 401 vs TLS
- Scanning `CrashReports`
- Changing the central UNC path default
- Blocking New System startup solely because Llog is unreachable
- Publishing from the DEV machine to `\\SI-WIN-2K19\AppFolder\AppNet\`

---

## 7. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Treat `CentralSinkEnabled` / folder tmp probe as proof of Llog | **Dropped** | 16.08: Danny local WARN + `CentralEnabled=true`, no `Client-20260816.log` on the share |
| Re-publish 1.0.32 heartbeat without read-back | **Dropped** | Same binary behavior; ops already proved Llog stayed empty |
| Abort startup if central write fails | **Dropped** | Operator asked for a **note** in «מצב מערכת», not a lockout |
| Second logger / Trace / Event Log heartbeat | **Dropped** | One Serilog pipeline |
| Whole-app MessageBox “cannot write log” | **Postponed** | Splash + status row first; revisit only if operators miss the row |
| Treat newest `Client-*.log` in the folder as “today” | **Dropped** | 16.08: reported marker missing in `Client-20260810.log` while today’s file did not exist |
| Lower heartbeat to Error so central Error still “proves” write | **Dropped** | Heartbeat stays Warning; central min stays Warning; הערה if DB/config disagrees |

---

## 8. Needs Review

1. If Slice D shows MSIX `runFullTrust` still cannot create a file that the ops PC sees on the UNC — packaging capability vs writing through a full-trust helper. Do not guess; measure on one packaged install.
2. Whether central should drop `WriteTo.Async` (local may keep Async). Shared `AddSiNetCentralLogging` impact on AccService/SyncEngine.
3. Exact splash Hebrew (copy in §3.2 is meaning-locked).
4. After ship: PROD Llog sweep must show `client-session-alive` for **each** installed station, not only Danny.
5. After Slice E: force `Logging.Client.CentralLevel` = Error on **DEV** DB only, start App.Wpf, confirm «מצב מערכת» shows the הערה and does **not** name yesterday’s file; restore Warning.

---

## 9. Acceptance (PROD)

1. Start `SiNet.App.Wpf` on a pilot PC (MSIX, not F5).
2. Splash shows the log-write check.
3. On the ops machine, `\\si-win-2k19\AutoCAD Data\log\Client\<that machine>\<that user>\Client-yyyyMMdd.log` contains `[STARTUP] Client process alive`.
4. If step 3 is forced to fail (share offline / ACL), «מצב מערכת» row `logging-central` is Degraded with Hebrew guidance — app still opens.
5. Llog skill class `client-session-alive` — not a defect.
6. If Client central min is not Warning, the `logging-central` row shows the §3.3 הערה. If today’s file is missing, Summary names **today’s** path only.

---

## 10. Change log

| Date | Change |
| --- | --- |
| 16.08.2026 | **Slice E (docs, for DEV):** also check applied central min is Warning; הערה via `GuidanceHe` if not; stop falling back to an old `Client-*.log`. PROD `Logging.Client.CentralLevel` restored to Warning; Danny `Client-20260816.log` then received the pid marker. |
| 16.08.2026 | **Implementing on development:** sync central File (drop Async); `StartupLogWriteVerifier` + splash + `logging-central` contributor; temp-dir tests. Slice D: sync File chosen so marker is visible without process exit. |
| 16.08.2026 | Planning: read-back startup check + System Status row. Evidence: 1.0.32 local heartbeat, empty Llog. |

---

## 11. Copy-paste for the DEV agent (Slice E)

Implement on **`development`**. Read this whole file. Reuse `StartupLogWriteVerifier` / `LoggingCentralStatusContributor` / `GuidanceHe`. No new health bus. No EF migration. Do not change AccService/SyncEngine levels. Do not lower the heartbeat to Error.

1. Expose last applied `CentralMinLevel` from `CentralLoggingBuilder`.
2. `ResolveTodayLogPath` — today’s dated name only; no newest-file fallback.
3. If applied Client central min ≠ Warning, set `GuidanceHe` to the §3.3 הערה (even on Idle).
4. If today’s central file is missing, Summary/Detail must name **today’s** path.
5. Tests in `StartupLogWriteVerifierTests` for Error-level + old-file fallback.
6. `dotnet build src\SiNet.App.Wpf\SiNet.App.Wpf.csproj` and `dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj`. Report build/test/DB (must be none).
