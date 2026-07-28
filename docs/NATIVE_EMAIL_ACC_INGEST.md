# Native Email → ACC Inbox ingest (standalone)

> Status: **N1 implemented** · **N2 implemented (awaiting operator smoke)** · **N3 recovery — implemented**  
> Date: 2026-07-28 (N1/N2/N3)  
> Approved by: operator (N1 + N2 + N3 chat 2026-07-28)


> Related: [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md),
> [`EMAIL_DETAIL_COMPONENT.md`](./EMAIL_DETAIL_COMPONENT.md),
> [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md),
> [`manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md`](./manual-tests/EMAIL_ACC_STANDALONE_SMOKE.md)

## Problem (verified)

Standalone `SiNet.App.Wpf` opens the Email surface, but ACC upload returns
`BackendNotAvailable` because **no** `IEmailAccIngestionExecutor` is registered.

| Host | Ingest executor |
| --- | --- |
| V2 | `LegacyEmailAccIngestionExecutor` → SiNetSQL `EmailIngestionService` |
| Standalone | *(none)* → coordinators return `BackendNotAvailable` |

Runtime probe (debug session 65884a): `ingestionExecutorRegistered: false` while Email window activated successfully.

## Principles (locked — do not reopen)

1. **ACC** = physical file truth; **Gmail label** = mailbox filed; **DB** = helper/cache only
   (see `EMAIL_ACC_SOURCE_OF_TRUTH.md`).
2. **AccService** stays a privileged ACC transfer/bootstrap backend — **not** a Gmail mailbox
   orchestrator. No new “ingest this Gmail message” AccService API in this slice.
3. Standalone host keeps **no** `ProjectReference` to SiNetSQL / V2.
4. Prefer **reuse** of existing clean ACC ports (`IAccFileUploadService`, inbox bootstrap,
   folder ensure) and Email Workbench coordinators already registered via `AddSiNetEmailAccSql`.

## Target state

```text
Email Workbench (App.Wpf)
  → EmailAccUploadCoordinator / EmailAccIngestQueue  (already registered)
  → IEmailAccIngestionExecutor  ← NEW: NativeEmailAccIngestionExecutor
       → Gmail load (native gateway / small port — not GoogleService from V2)
       → lease + identity + AccInboxLayout (native)
       → AccService Remote: inbox ensure + folder path + file upload
       → optional body PDF (native renderer or best-effort skip)
       → DB cache upsert (helper only)
```

UI / queue / status / completion wait stay as today. Only the **host executor** gap is filled.

### Same pattern as Move

`NativeEmailMoveToProjectExecutor` already proves the pattern: Application port +
Infrastructure.Sql implementation over `IAcc*` ports. Ingest follows that shape.

## Slice scope (proposed)

### In scope — Slice N1 (minimum viable ACC upload)

1. Document approval (this file).
2. `NativeEmailAccIngestionExecutor` implementing `IEmailAccIngestionExecutor` in
   `SiNet.Infrastructure.Sql` (or adjacent Infrastructure project if Gmail deps force a split).
3. Register it from standalone composition (`AddSiNetStandaloneHost` / `AddSiNetEmailAccSql`
   host extension) — **not** only from V2.
4. Native path to load message + attachment bytes for upload (extend existing Email/Gmail
   ports; no SiNetSQL `GoogleService` ctor).
5. Inbox ensure + folder layout + `IAccFileUploadService` for PDF (if available) + attachments.
6. Lease / AlreadyProcessed / InProgress parity sufficient for multi-user safety
   (reuse existing SQL lease helpers if already ported; otherwise minimal port).
7. Map outcomes to existing `EmailAccUploadResult` / Hebrew display strings.
8. Tests: DI registration in standalone; BackendNotAvailable removed when executor present;
   focused unit tests for identity/layout gates (not full ACC integration).

### Explicitly out of scope for N1

- Moving orchestration into AccService HTTP
- Porting full SiNetSQL `EmailIngestionService` as a ProjectReference
- `IEmailExternalDownloadExecutor` / `IEmailAccRecoveryExecutor` native ports
  (follow-up N2 — same optional-DI pattern)
- Registering `NativeEmailMoveToProjectExecutor` in standalone (follow-up N2 unless
  needed for smoke of upload→move in the same pilot)
- Perfect PDF parity if WebView2 renderer is heavy — document “body PDF best-effort”
- Deleting V2 `LegacyEmailAccIngestionExecutor` (keep as Legacy bridge)

## Options considered

| Option | Decision |
| --- | --- |
| **A. Native executor over existing ACC ports** | **Selected** — matches Move pattern and standalone host lock |
| B. Full ingest API on AccService | Rejected — wrong boundary (Gmail/PDF on server) |
| C. Keep V2-only forever | Rejected — contradicts standalone New System goal |

## Risk & complexity (pre-code)

| Item | Assessment |
| --- | --- |
| Complexity | **High** — orchestration + Gmail bytes + ACC ensure/upload + lease/DB cache |
| Effort | Multi-step (N1 then N2); expect several focused PRs, not one dump |
| Behavior drift | High risk vs 98KB legacy `EmailIngestionService` — mitigate with SoT rules + outcome parity tests |
| Auth dualism | Gmail token store for New System vs legacy `GoogleService` — must use native auth path already used by Email list |
| AccService | Requires AccService running (Remote) for inbox ensure/upload — already assumed by MultiStart profile |
| Host BaseUrl | Standalone `appsettings.json` defaults to `https://localhost:8443` (AccService launch profile). DB system setting `AccServiceBaseUrl` overrides when set. Empty BaseUrl → Local mode → `AccBootstrapLocalInboxBootstrapExecutor` (StandaloneNew). Prefer Remote for MultiStart. |
| DB/schema | **None** expected for N1 |
| Breaking V2 | Low if Legacy executor stays registered in V2 |

### Implementation steps (N1) — code gate

Confirmed gaps before coding:

1. **`IEmailGateway` has no attachment-bytes API** today (`GetDetailsAsync` = metadata + body text only). N1 must add e.g. `DownloadAttachmentAsync(messageId, attachmentId)` on the gateway + `GmailEmailGateway`.
2. **Lease helpers** exist as fields/mappers (`ProcessingByLogin`, `EmailAccLeasePolicy`) but **no native acquire/release service** yet — must add minimal lease write path in Infrastructure.Sql.
3. **ACC ports ready:** `IAccInboxBootstrapService`, `IAccFolderPathService`, `IAccFileUploadService` (Remote via AccService).
4. **PDF:** no native `IEmailPdfRenderer` in App.Wpf — N1 = best-effort skip / optional later, attachments still upload.

Proposed code order:

| Step | Work |
| --- | --- |
| 1 | Gmail attachment download port on `IEmailGateway` + implementation |
| 2 | `NativeEmailAccIngestionExecutor` (+ small helpers: lease, identity, DB cache upsert) |
| 3 | DI register in standalone (`AddSiNetEmailAccSql` or standalone host extension) |
| 4 | Tests + build gate; smoke with AccService MultiStart |
| 5 | Follow-up N2: external download / recovery / Move registration |

**Code starts only after explicit operator go-ahead on this step list.**

## Acceptance criteria (N1)

1. Standalone Email → explicit “העלה ל-ACC Inbox” (or passive ingest when attachments exist)
   does **not** show `BackendNotAvailable` / “Backend לא מוגדר” when AccService + Gmail session are healthy.
2. Successful upload creates physical items under Office Inbox layout
   (`_Inbox/THREAD_…/MSG_…/` + `Attachments/`) per `AccInboxLayout`.
3. DB cache updated as helper; ACC remains SoT for physical presence.
4. V2 Legacy path unchanged.
5. Build gate: `SiNetProjectManagerV2` + `SiNet.App.Wpf.Tests` pass; no EF migrations.

## Approval gate

**Do not implement N1 until this document is explicitly approved** (and any scope tweaks
recorded below).

### Approval notes (N1)

- Approved by: operator
- Date: 2026-07-28
- Scope tweaks: none

---

## Slice N2 — MoveToProject + Jumbo/external download (standalone)

### Problem (verified)

| Gap | Standalone today | V2 |
| --- | --- | --- |
| **Move** | `NativeEmailMoveToProjectExecutor` exists but is registered **only in V2** `App.xaml.cs` → coordinator returns unavailable | Registered Transient |
| **Jumbo → ACC** | Link chips can open system browser; **no** `IEmailExternalDownloadBrowserHost` + **no** `IEmailExternalDownloadExecutor` → no download capture / ACC upload | `V2EmailExternalDownloadBrowserHost` + `LegacyEmailExternalDownloadExecutor` (SiNetSQL) |
| **Recovery** | Was V2-only → **N3** registers native executor | `LegacyEmailAccRecoveryExecutor` |

N1 already uploads Gmail attachments via AccService Remote. Move and Jumbo need the same host registration / native upload path without SiNetSQL.

### Principles (same as N1 — locked)

1. ACC physical SoT; DB helper; Gmail label = mailbox filed.
2. No new AccService “Gmail orchestrator” API.
3. No `ProjectReference` from App.Wpf → SiNetSQL / V2.
4. Reuse `IAcc*` ports + existing coordinators (`EmailMoveToProjectCoordinator`, `EmailExternalDownloadCoordinator`, `EmailExternalDownloadHandler`).

### Target state

```text
Move:
  ActionBar / Workbench
    → IEmailMoveToProjectService / Coordinator  (already registered)
    → IEmailMoveToProjectExecutor ← register NativeEmailMoveToProjectExecutor in AddSiNetEmailAccSql

Jumbo:
  Email body link chip
    → IEmailExternalDownloadBrowserHost  ← NEW App.Wpf WebView2 download window
         (DownloadStarting → local temp path → DownloadCompleted event)
    → EmailExternalDownloadHandler (already)
    → IEmailExternalDownloadCoordinator (already)
    → IEmailExternalDownloadExecutor ← NEW NativeEmailExternalDownloadExecutor
         → ensure inbox layout (same as N1) + IAccFileUploadService
         → DB attachment row with IsExternalDownload = true
         → ZIP: extract + multi-file upload (parity with Legacy)
```

### In scope — N2

| Sub-slice | Work |
| --- | --- |
| **N2-Move** | Register `NativeEmailMoveToProjectExecutor` in `AddSiNetEmailAccSql` (standalone + V2 last-wins OK). Confirm deps already in standalone DI (`IProjectFileFilingService`, ACC download/upload/browser/metadata, `ITaskCompletionService`). Update tests that assert “Move executor V2-only”. Smoke: tagged attachments → Move → project folder. |
| **N2-Jumbo-Executor** | `NativeEmailExternalDownloadExecutor` in Infrastructure.Sql over same ACC/layout/lease helpers as N1. Single file + ZIP extract. Map to `EmailExternalDownloadResult`. Register Transient in `AddSiNetEmailAccSql`. Keep V2 Legacy executor registration for V2 host (last wins). |
| **N2-Jumbo-Browser** | App.Wpf `IEmailExternalDownloadBrowserHost`: dedicated WebView2 window, intercept downloads, raise `DownloadCompleted`, `ReportProgress`. Register Singleton in `AddSiNetNewSystemWpf` / standalone host. Wire `EmailWindowViewModel` handler (coordinator + host both present). |
| **Docs/UI** | Update `EMAIL_DETAIL_COMPONENT.md`: full pipe on standalone; system-browser fallback only when host missing. |

### Explicitly out of scope for N2

- `IEmailAccRecoveryExecutor` native (N3)
- Porting entire `WebView2Helper` / shared Gmail cookie profile (N2 browser may use a dedicated user-data folder; operator may need to log into Jumbo/WeTransfer in that window)
- Perfect parity with V2 `DownloadAssociationDialog` project-file association UI (N2 associates to the **current email’s ACC inbox**, then Move tags as today)
- Deleting V2 Legacy/bridge types
- Master Plan, G-Policy send/reply

### Options

| Option | Decision |
| --- | --- |
| **A. Register existing Native Move + new native Jumbo executor/host** | **Selected** |
| B. Keep opening system browser only (no ACC upload) | Rejected — not production Move/Jumbo |
| C. Call Legacy via SiNetSQL from App.Wpf | Rejected — host boundary |

### Risk & complexity (pre-code)

| Item | Assessment |
| --- | --- |
| **N2-Move complexity** | **Low** — code exists; mainly DI + smoke |
| **N2-Jumbo complexity** | **Medium–High** — new browser host + native upload/ZIP + DB external-download rows |
| Effort | Prefer **two PRs**: (1) Move registration, (2) Jumbo executor+browser |
| Behavior drift | ZIP / subfolder naming vs Legacy — match Legacy where possible; document deltas |
| WebView2 downloads | Must use `DownloadStarting` / path override; test Jumbo + WeTransfer manually |
| AccService | Same Remote requirement as N1 |
| DB/schema | **None** (`IsExternalDownload` already exists) |
| Breaking V2 | Low if V2 keeps registering Legacy Jumbo + same Native Move |

### Implementation order (after approval)

1. **N2-Move** — DI + tests + operator smoke Move  
2. **N2-Jumbo-Executor** — native upload from local path (unit-testable without UI)  
3. **N2-Jumbo-Browser** — App.Wpf host + DI + end-to-end smoke  
4. Build gate + update this doc status to implemented  

### Acceptance criteria (N2)

1. Standalone: Move on a filed message with tagged ACC attachments does **not** return BackendNotAvailable; files land under project filing rules (ACC Move/Lock SoT).  
2. Standalone: Jumbo/WeTransfer chip opens in-app browser; after download, file uploads to ACC Inbox and appears on the attachment strip as external download.  
3. ZIP: multi-file upload or clear failure message (Legacy parity).  
4. V2 Legacy Jumbo path still works.  
5. Build gate green; no EF migrations.

### Approval gate (N2)

**Do not implement N2 until this section is explicitly approved.**

### Approval notes (N2)

- Approved by: operator
- Date: 2026-07-28
- Scope tweaks: none

---

## Slice N3 — Native ACC inbox recovery (standalone)

### Problem (verified)

| Gap | Standalone today | V2 |
| --- | --- | --- |
| **Recovery** | `IEmailAccRecoveryExecutor` not registered in `AddSiNetEmailAccSql` → `SqlEmailAccStatusService` skips repair when reconciliation reports `MissingInAcc` | `LegacyEmailAccRecoveryExecutor` → SiNetSQL `AccInboxRecoveryService` |

N1 already re-uploads Gmail attachments. Recovery only needs: clear stale Acc ids → call ingest → verify.

### Target state

```text
EmailAccSelectionHandler
  → SqlEmailAccStatusService.SyncStatusWithRecoveryAsync  (already)
  → IEmailAccRecoveryExecutor
       ├─ AddSiNetEmailAccSql → NativeEmailAccRecoveryExecutor
       └─ V2 last-wins → LegacyEmailAccRecoveryExecutor (kept)
```

### In scope — N3

1. `NativeEmailAccRecoveryExecutor` in Infrastructure.Sql:
   - External-download-only guard (Hebrew message; no AccId clear / no ingest)
   - Clear `AccItemId`/`AccVersionId` on missing rows; force message `Error` if Uploaded/Moved
   - `IEmailAccIngestionExecutor.IngestToInboxAsync` (reuse N1)
   - Verify requested ids regained `AccItemId`; log failures
2. Register Transient in `AddSiNetEmailAccSql`
3. Tests: DI registration + source/guard asserts
4. Docs status update

### Explicitly out of scope for N3

- Progress events (`RecoveryProgressChanged`)
- Deleting V2 Legacy / SiNetSQL `AccInboxRecoveryService`
- Re-download of Jumbo/external files (use N2 path)
- UI / selection-handler changes
- EF migrations

### Options

| Option | Decision |
| --- | --- |
| **A. Native recovery over N1 ingest** | **Selected** |
| B. Call SiNetSQL AccInboxRecoveryService from App.Wpf | Rejected — host boundary |
| C. Leave standalone without recovery | Rejected — MissingInAcc never heals |

### Risk & complexity

| Item | Assessment |
| --- | --- |
| Complexity | **Medium** — short orchestration; upload already in N1 |
| Effort | One focused PR |
| Behavior drift | Full re-ingest may upload other AccId-less rows — Legacy parity |
| AccService | Same Remote requirement as N1 |
| DB/schema | **None** |
| Breaking V2 | Low — Legacy registration last-wins |

### Acceptance criteria (N3)

1. Standalone: message with Gmail attachment `MissingInAcc` recovers AccItemId after sync (AccService + Gmail healthy).
2. External-download-only missing set → no AccId clear / no ingest.
3. V2 Legacy recovery still registered.
4. Build gate green; no EF migrations.

### Approval notes (N3)

- Approved by: operator
- Date: 2026-07-28
- Scope tweaks: none

