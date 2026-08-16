# לוגים מהותיים ל-Llog (הטמעה)

> **Title:** Material failures must reach the central UNC log at Warning+  
> **Date:** 13.08.2026  
> **Updated:** 16.08.2026  
> **Status:** Implementing (P0+P1 code landed; Client heartbeat locked 16.08; validate in production Llog after publish)  
> **Scope:** What must appear on `\\si-win-2k19\AutoCAD Data\log` during rollout/tuning when a **material** operation fails, plus the Client session heartbeat. Host: `SiNet.App.Wpf` + AccService paths those clients call.

Related: [`LOGGING.md`](./LOGGING.md) (pipeline / §9 central sink), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) (ops tails), [`OPS_LLOG_REVIEW.md`](./OPS_LLOG_REVIEW.md) (agent sweep), [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) (severe token notice), [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md), [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md), [`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md) (DEV-027 — Gmail timeout noise).

---

## 1. Why

During rollout, operators diagnose from **Llog** (central Serilog share), not from each workstation’s local file. Client central default is **Warning** (`Logging.Client.CentralLevel`). After the 16.08 heartbeat ships, a healthy start **must** produce `[STARTUP] Client process alive` + sink diagnostics on Llog ([`LOGGING.md`](./LOGGING.md) §9.4.1). **1.0.32 did not meet this on the share** (local WARN only) — [`DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md`](./DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md) (DEV-028). Missing heartbeat after that ship is a **write-verify** miss, not “healthy silence”. Material failures still must be Warning/Error/Fatal.

Observed gap (code review 13.08.2026): several ACC / FileMaterial / Gmail-filing failures are visible in the UI (`MessageBox` / Status) or in `System.Diagnostics.Trace`, and **never** reach Llog. Example the operator named: failing to upload to ACC is a material failure; today MoveToProject records that as `Trace.TraceWarning` / `Trace.TraceError`.

---

## 2. Existing mechanism (reuse — do not add a parallel logger)

| Piece | Where | Role |
| --- | --- | --- |
| Central UNC sink | `AddSiNetCentralLogging` in `SiNet.Infrastructure.Logging` | `{CentralLogPath}\{App}\{Machine}\{User}\{App}-yyyyMMdd.log` |
| Client identity | `SiNetApp.Client` via `StandaloneHostLoggingBootstrap.ConfigureCentral` | Folder `Client\...` after vault SQL is available |
| AccService / SyncEngine | Their `Program.cs` | Same module; own app folders |
| Application port | `IAppLogger` (`Info` / `Warn` / `Error`) → `SerilogAppLogger` | **The** port for New System services. `Warn`/`Error` reach Llog at default Warning |
| UI unexpected | `AppErrorReporter` → host forwards to `IAppLogger` | Unhandled WPF; not a substitute for operation-outcome logs |
| `System.Diagnostics.Trace` | MoveToProject, ProjectFileFiling, WPF dashboards, metadata | **No bridge to Serilog in this repo.** Does not appear on Llog |
| `WorkflowDebugTrace` | Opt-in `SINET_WF_DEBUG` | Local + Trace only — not Llog |
| Per-user `LoggingEnabled` | Local sink level switch only | Must **not** silence central ([`LOGGING.md`](./LOGGING.md) §9.1) |

**Do not** lower `Logging.Client.CentralLevel` (or AccService/SyncEngine central) to Information. Raise the **failure** to Warning/Error instead. Pilot decision 02.08.2026: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) §2.1.

---

## 3. Target principles (locked for a later code round)

### 3.1 Level

| Level | When |
| --- | --- |
| **Error** | The user-facing operation **failed**: no physical file in ACC, Ensure Inbox failed, Gmail File/Unfile failed, MoveToProject run outcome Failed, upload/ensure-path threw, workflow action execution failed |
| **Warning** | **Partial** success or recoverable gap: one attachment failed and the run continued; Move/Lock metadata missing after the file exists; empty ZIP / local file missing before upload |
| **Information** | Success summaries, bootstrap timeline **on success**, ingest “uploaded AccItemId=…” — **stay Information** (not Llog by design) |
| **Debug** | Connector restore noise, NDJSON debug sinks — never Llog |

Fatal remains startup/schema/crash only.

### 3.2 What is material (must reach Llog)

Aligned with [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md):

1. **Physical ACC** — upload to Inbox or project folder failed; Ensure Inbox / EnsurePath failed; download from Inbox before Move failed; FileMaterial/MoveToProject run did not file required items.
2. **ACC Move/Lock metadata** — write failed after physical presence (Warning if file exists; Error if that blocks FileMaterial).
3. **Mailbox SoT** — Gmail project label File / Unfile failed (`IEmailFilingService`).
4. **Blocking platform** — vault/SQL/schema already Fatal/Warning; connector auth restore that leaves Google/ACC unusable (Warning, not Debug).
5. **AccService Autodesk refresh token missing/stale** — **Error** on AccService and on the Client operation. **Severe UI** for the user (cannot continue ACC work without being told). See [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) lock 16.08.2026. Not TLS and not workstation API key.

### 3.3 What must not inflate Llog

- Empty attachment skipped; “no body — skip 00_Email.pdf”
- EF `QuerySplittingBehavior` / FK navigation split warnings
- Session opened/closing lifecycle (V2 used Warning for this; **do not copy** as a flood of per-session lines)
- AccService favicon / missing API key on anonymous probes
- System Status 10s Gmail `OperationCanceledException` (DEV-027 Slice A — must not stay `Log.Error`)
- Per-user local Debug; `WorkflowDebugTrace`; agent NDJSON under `%TEMP%`

### 3.4 Line shape (for `llog-review` fingerprints)

One line per **outcome** (not a stack of decorative boxes). Tag + fields:

```text
[MoveToProject] outcome=Failed inbox={id} project={id} moved={n}/{total} kind={FilingFailed|MissingInAcc|…} att={id} file='…' http={code} detail=…
[AccUpload] outcome=Failed project={siNetProjectId} accProject={…} displayName='…' folder={…} http={code} detail=…
[EnsureInbox] outcome=Failed user={…} projectName='…' detail=…
[EmailFiling] outcome=Failed op=FileToProject gmailMsg={id} project={id} detail=…
[NativeAccIngest] outcome=Failed msgUniqueId={…} inboxId={…} error=… durationMs=…
```

Reuse existing tags where they already exist (`[MoveToProject]`, `[NativeAccIngest]`, `[AccService]`). Do not invent a second tag for the same operation.

### 3.5 Client vs AccService (avoid double noise)

| Layer | Logs |
| --- | --- |
| **AccService** | HTTP handler that talks to Autodesk: Error on upload/ensure **in the AccService folder** (`projectId`, `displayName`, exception) |
| **Client** | The **business operation** that the user ran: Error/Warning with inbox/project/attachment ids **in the Client folder** |

If AccService already logged `ACC file upload failed…` and the client only rethrows, the client **still** logs the business outcome (`[AccUpload]` / `[MoveToProject]`) — operators tail **Client** for the user, not AccService. Do not also dump the full Autodesk HTML body on both sides.

### 3.6 UI is not a log

`MessageBox`, Status, `LoadWarning`, and `EmailMoveToProjectOutcomeDisplay` stay for the user. They do **not** replace `IAppLogger.Warn`/`Error`. A failure that only updates Status is a documentation gap in the table below.

**Token Critical (16.08.2026):** if AccService has no valid Autodesk refresh token, Status/«מעלה ל-ACC…» is **not** enough. The user must get a severe notice and must not continue that ACC action. Log Error as well.

---

## 4. Gap catalogue (As-Is → Target)

Code as of 13.08.2026. **This round does not change code.** Target is for a later implementation slice after this document is approved.

### 4.1 P0 — Physical ACC / FileMaterial / ingest

| Operation | Where (As-Is) | Today | Target |
| --- | --- | --- | --- |
| MoveToProject / FileMaterial | `NativeEmailMoveToProjectExecutor` — download/file/metadata ~179–388, 582–641; run Failed ~441–448; `MissingAccItemId` ~306–309 often **no** Trace; reconcile ~517 Trace | `Trace.TraceWarning` / `Trace.TraceError`; UI MessageBox. **No `IAppLogger`.** | Inject `IAppLogger`. **Error** on run Failed and per-attachment file/download throw. **Warning** on skip/lock/partial continue and `MissingAccItemId`. One outcome line §3.4 |
| Coordinator unavailable | `EmailMoveToProjectCoordinator` BackendNotAvailable | Result DTO only | **Error** `[MoveToProject] outcome=Failed kind=BackendNotAvailable` |
| File to ACC folder | `ProjectFileFilingService.FileToAccAsync` / `ResolveAccMappingAsync` ~178–286; `RemoteAccFileUploadService.UploadAsync` | EnsureMapping: Trace + throw; client upload: `EnsureSuccessStatusCode` **no** client Serilog. AccService upload **does** `Log.Error` (`AccEndpoints` ~386) | Client **Error** `[AccUpload]` with project/displayName/http. Keep AccService Error |
| Move/Lock metadata | `RemoteAccItemMetadataService` Read/Write Fail ~77, 136 | Trace on exception; HTTP Fail often DTO only | **Warning** if file exists; **Error** if FileMaterial blocked. `[AccMetadata] op=Write item=… http=…` |
| Native ingest to Inbox | `NativeEmailAccIngestionExecutor` | Attachment fail **Warn** (~408) — already Llog. Early `Failed` (~89–113, 196–241) and `FailAndReleaseAsync` (~909–932, including swallowed `SaveChanges`) **no** Warn. UI `EmailAccSelectionHandler` Status only | **Error** on every Failed return (including `FailAndReleaseAsync`). Keep per-attachment Warn. Success stays Info |
| External download → Inbox | `NativeEmailExternalDownloadExecutor` | Upload exception **Warn** (~374). Early Failed (missing file, empty ZIP, bootstrap, DB) **no** log | **Error** bootstrap/upload; **Warning** empty/local-missing |
| Body PDF render | `WpfEmailBodyPdfRenderer` | Trace only (~111, 143, 182, 322). Ingest may Warn if renderer missing | **Warning** if required PDF path failed (ingest already continues) |
| Ensure Office Inbox | AccService `POST /v1/acc/inbox/ensure` (`AccEndpoints` ~794–854); `AccBootstrapService.LogBootstrapTimeline` ~232–274; client `RemoteAccInboxBootstrapService` | Timeline **all** `AccBootstrapLog.Info` including FAILED + Error snippet — **not** Llog. Endpoint catch returns BadRequest **without** `Log.Warning/Error`. Client throws without Serilog | AccService **Error** on catch + one FAILED summary (not the ASCII box at Info). Client **Error** `[EnsureInbox]` on HTTP/empty ids |
| EnsurePath (message / Attachments folder) | `RemoteAccFolderPathService` | HTTP fail throws; only logged if outer ingest catch Warns | **Error** `[AccEnsurePath] project=… root=… http=…` at the remote client |

### 4.2 P0 — Mailbox SoT (Gmail label)

| Operation | Where | Today | Target |
| --- | --- | --- | --- |
| File / Unfile to project | `SqlEmailFilingService.FileToProjectAsync` / `UnfileFromProjectAsync` | `return EmailFilingResult(false, …)` without Warn; compensation `Debug.WriteLine` | **Error** on File/Unfile API or result false (except invalid command that never left the machine — **Warning**). **Warning** on compensation fail. `[EmailFiling]` §3.4 |
| CreateLabel on filing path | `GmailEmailModifyService` | throw + `WorkflowDebugTrace` | **Error** before rethrow `[GmailModify] CreateLabel failed path='…'` |
| UI filing coordinator | `EmailListFilingCoordinator.ExecuteRowActionAsync` | `LoadWarning` / Status | Prefer executor log; UI **Warning** only if the service returned Failed **and** did not log (defense in depth) |

### 4.3 P1 — Workflow, shell, restore

| Operation | Where | Today | Target |
| --- | --- | --- | --- |
| Workflow action run | `WorkflowActionExecutor` | `Trace.TraceError` | **Error** `[WorkflowAction] instance=… action=…` |
| Project continuation start | `SqlProjectTypeContinuationStarter` | `Trace.TraceError` | **Error** |
| Projects dashboard refresh/open/edit | `ProjectsDashboardViewModel` | Trace | **Warning** (user-visible failure) |
| Work surface launch missing host | `WorkSurfaceLauncher` | Trace (includes “not registered”) | **Warning** for missing route/host at runtime; **do not** log every DEBUG-only missing optional service |
| Email list label sync | `EmailListViewModel` | Trace | **Warning** |
| Connector auth restore at startup | `App.xaml.cs` `StartConnectorAuthRestore` | **Debug** | **Warning** `[AuthRestore] {Connector} failed: …` (not canceled-timeout spam) |
| Remote ACC missing BaseUrl/ApiKey | `RemoteAccFileUploadService` / Inbox / FolderPath | throw, log depends on caller | **Error** `[AccRemote] missing ApiKey\|BaseUrl op=…` before throw |

### 4.4 Already on Llog (do not “fix”)

| Signal | Notes |
| --- | --- |
| Vault missing / SQL schema pending | `App.xaml.cs` Warning / Fatal |
| `[AccService] EnsureInboxAsync FAILED` on **client** when the **legacy/V2** provisioner logs HTTP (Lilach 401 sample) | New System remote path still needs §4.1 EnsureInbox client Error |
| AccService `ACC file upload failed for project …` | Keep |
| `[NativeAccIngest] Attachment failed '…'` Warn | Keep |
| Gmail list/send Error | Keep real API failures; DEV-027 will stop treating System Status cancel as Error |
| `[UI]` / Fatal unhandled | Keep |

---

## 5. Recommended code slices (after this document is approved)

Not in this round. Order:

1. **P0a** — `NativeEmailMoveToProjectExecutor` + coordinator: `IAppLogger`; one run-outcome line; replace Trace on file/download/metadata.
2. **P0b** — `SqlEmailFilingService` + `FailAndReleaseAsync` / early Failed in ingest and external download.
3. **P0c** — AccService `/inbox/ensure` Error; `LogBootstrapTimeline` FAILED at Error (success stays Info); client `[EnsureInbox]` / `[AccUpload]` / `[AccEnsurePath]` / `[AccMetadata]`.
4. **P1** — Workflow executor, auth restore, dashboard/launcher as in §4.3.
5. Tests: assert `IAppLogger.Warn`/`Error` (or test sink) on Failed outcomes — **not** UNC I/O.

Build gate when code lands: `dotnet build src\SiNet.App.Wpf\SiNet.App.Wpf.csproj` and `dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj`. No DB/schema change.

---

## 6. Out of Scope

- Changing `Logging.Client.CentralLevel` / AccService / SyncEngine central defaults
- Bridging **all** `Trace` in the solution (only material operations in §4)
- In-app log viewer; Seq/HTTP telemetry
- Scanning `CrashReports` from this document
- Implementing DEV-027 slices
- Closing agent `artifacts/llog-review/pending.json` fingerprints
- Lowering success/lifecycle Information to Warning except the optional P2 in §8
- New logging framework, second `IAppLogger`, or writing Trace to UNC

---

## 7. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Lower central level to Information so “everything shows up” | **Dropped** | Noise; pilot lock 02.08.2026; raise failures instead |
| Treat UI Status / MessageBox as sufficient ops evidence | **Dropped** | Operators read Llog, not the user’s screen |
| Bridge every `Trace.TraceWarning` in WPF (PDF init abort, ReadOnly clear, FileServer Hidden) | **Postponed** | Not material ACC/mailbox SoT; revisit if ops asks |
| ASCII AccBootstrap timeline boxes at Warning | **Dropped** | Inflates Llog; one Error line on FAILED is enough |
| Duplicate AccService upload Error into a second identical Client stack dump | **Dropped** | Client logs business outcome; AccService logs Autodesk I/O |

---

## 8. Needs Review

1. **Locked 16.08.2026:** Client session heartbeat at **Warning** (`[STARTUP] Client process alive` + sinks + ready) — [`LOGGING.md`](./LOGGING.md) §9.4.1. Not V2 opened/closing GUIDs.
2. Whether AccService EnsureInbox Error should include the truncated `_docsLastError` only, or also `projectName` (prefer both, cap length).
3. `WorkSurfaceLauncher` “service not registered” in production vs DEBUG — log only when the user actually invoked that route.

---

## 9. Change log

| Date | Change |
| --- | --- |
| 16.08.2026 | **DEV-028 Planning:** 1.0.32 local heartbeat, empty Llog — read-back + System Status ([`DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md`](./DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md)). |
| 16.08.2026 | Operator lock: Client **Warning** heartbeat every start (alive + sinks connected). Quiet Information-only sessions are no longer acceptable. |
| 14.08.2026 | **Code P0a–P1:** Material failures → `IAppLogger`/Serilog Warning+/Error (MoveToProject, Filing, Ingest, ExternalDownload, AccService EnsureInbox, Remote Acc*, WorkflowAction, AuthRestore, dashboard/launcher/email list). |
| 13.08.2026 | Documentation-only: principles + gap catalogue. No code. |
