# Native Email → ACC Inbox ingest (standalone)

> Status: **Approved — N1 implemented (awaiting operator smoke)**  
> Date: 2026-07-28  
> Approved by: operator (chat 2026-07-28)  


> Related: [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md),
> [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md)

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
| Host BaseUrl | Standalone `appsettings.json` defaults to `https://localhost:8443` (AccService launch profile). DB system setting `AccServiceBaseUrl` overrides when set. Empty BaseUrl → Local mode → bootstrap fails without local executor. |
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

### Approval notes

- Approved by: operator
- Date: 2026-07-28
- Scope tweaks: none
