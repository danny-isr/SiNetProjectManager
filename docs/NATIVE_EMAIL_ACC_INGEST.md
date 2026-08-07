# Native Email → ACC Inbox ingest (standalone)

> Status: **N1–N3 implemented** · **N4 body PDF — implemented** · **N4.3 eligibility + no-redundant-upload — implemented** · **N5 ZIP/RAR — proposed (docs only)**
> Date: 2026-07-28 (N1/N2/N3) · 2026-07-31 (N4 / N4.1 / N4.3 / N5)
> Approved by: operator (N1–N3); N4 code go-ahead 2026-07-31; N4.3 code go-ahead 2026-07-31; N5 code awaits approval


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

**Follow-up (pilot 03.08.2026):** body HTML clicks still navigate in-place and bypass this window — track as **DEV-001** in [`DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md`](./DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md) / [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

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

---

## Slice N4 — Zero-attachment ingest + `00_Email.pdf` (standalone)

### Problem (verified 2026-07-31 soak)

Project-associated / selected emails with **no Gmail attachments** do not create ACC Inbox state.

| Gap | Today (Native) | Principles / target |
| --- | --- | --- |
| Zero attachments | `NativeEmailAccIngestionExecutor` returns `SkippedNoAttachments` before folder ensure | Per-message folder must exist (`THREAD_…/MSG_…/`) |
| Body PDF | Not implemented on Native path | `00_Email.pdf` in message folder (`AccInboxLayout.EmailBodyFileName`) |
| UI gates | `EmailAccSelectionHandler` skips passive ingest / Upload when `!HasAttachments` | Allow ingest when message should land in ACC Inbox |
| Legacy parity | SiNetSQL also skips no-attachment emails | **Intentional New-System correction** — do not reopen Legacy in this slice |

Operator decision (2026-07-31): **option 2 — full layout** (folder + body PDF best-effort), not folder-only.

### Principles (locked for N4)

1. Same SoT as N1: ACC physical; Gmail label = mailbox filed; DB helper.
2. Layout per `EmailSystemPrinciples` §6.2 / `AccInboxLayout`: message folder contains `00_Email.pdf`, `manifest.json`, and `Attachments\` (may be empty).
3. Body PDF is **best-effort**: missing renderer / render failure must **not** fail ingest if the message folder (+ optional attachments) was ensured. Log and continue.
4. No SiNetSQL / V2 `ProjectReference` for PDF. New Application port + App.Wpf WebView2 impl.
5. Do **not** change SiNetSQL `EmailIngestionService` STEP A in this slice (Legacy may keep skipping).

### Target state

```text
EmailAccSelectionHandler / explicit Upload
  → NativeEmailAccIngestionExecutor
       → load Gmail details (attachments may be empty)
       → lease + ensure THREAD_/MSG_ (+ Attachments/ when needed)
       → IEmailBodyPdfRenderer? → upload 00_Email.pdf (best-effort)
       → upload Gmail attachments (0..n)
       → manifest.json (best-effort)
       → Succeeded when message folder is on ACC (body PDF optional; attachments optional)
```

### In scope — N4

1. Application port `IEmailBodyPdfRenderer` (`IsAvailable`, `RenderHtmlToPdfAsync`) — thin; not `IEmailBodyRenderer`.
2. App.Wpf implementation (hidden WebView2 print-to-PDF; adapt from V2 `WebView2PdfRenderer.RenderToPdfAsync` without SiNetSQL types). Singleton DI in `AddSiNetNewSystemWpf`.
3. `NativeEmailAccIngestionExecutor`:
   - Remove `SkippedNoAttachments` early exit.
   - Ensure message folder even when `attachments.Count == 0`.
   - Best-effort body PDF upload to **message folder** (`AttachmentIndex` sentinel matching Legacy `-11` if DB requires a row).
   - Change “`uploadedCount == 0` ⇒ Failed” so zero-attachment success is allowed when folder (+ optional PDF/manifest) succeeded.
   - Short-circuit `AlreadyProcessed`: treat Uploaded/Moved with `InboxAccFolderId` as done even if attachment count is 0.
4. UI: allow `CanUpload` / passive ingest when connected and not terminal — **do not** require `HasAttachments`.
5. Docs + boundary/unit tests (source guards + DI). Smoke: file/select a no-attachment project email → ACC MSG folder + `00_Email.pdf` when renderer available.

### Explicitly out of scope for N4

- Changing Legacy / SiNetSQL skip-no-attachment behavior
- Perfect WYSIWYG parity with live Gmail DOM print
- Forcing Move of empty emails into project file slots (Move empty shortcut already exists)
- EF migrations / schema changes
- Requiring body PDF for `Succeeded` (best-effort only)

### Options

| Option | Decision |
| --- | --- |
| **A. Folder + best-effort `00_Email.pdf`** | **Selected** (operator option 2) |
| B. Folder/SQL only, no PDF | Rejected for this slice |
| C. Keep `SkippedNoAttachments` | Rejected — contradicts soak requirement |

### Risk & complexity (pre-code)

| Item | Assessment |
| --- | --- |
| Complexity | **Medium–High** — new WebView2 PDF port + ingest success criteria change |
| Effort | Focused PR; PDF init may need STA/dispatcher care like V2 |
| Behavior drift | Native diverges from Legacy skip — intentional; document in smoke |
| UI | Passive ingest may run more often (every selected email) — watch AccService load |
| AccService | Same Remote requirement as N1 |
| DB/schema | **None** expected |
| Breaking V2 | Low if Native-only; V2 still uses Legacy skip unless later ported |

### Acceptance criteria (N4)

1. Standalone: select/upload a **no-attachment** email → ACC has `THREAD_…/MSG_…/` (and `00_Email.pdf` when renderer available).
2. With attachments: still uploads attachments; body PDF best-effort; does not regress N1.
3. UI no longer blocks Upload solely because `HasAttachments == false`.
4. Build gate green; no EF migrations.

### Approval notes (N4)

- Direction: operator chose option 2 (2026-07-31)
- Code go-ahead: operator (2026-07-31)

### N4.3 — ACC ingest eligibility + no redundant re-upload (2026-07-31)

**Operator direction:** reduce AccService / WebView2 load. Supersedes the N4 UI rule “passive-ingest every selected email including unfiled zero-attachment”.

#### Eligibility (when to ingest)

| Condition | Passive select / Upload button | After mailbox File-to-project (§6.6) |
| --- | --- | --- |
| Has business Gmail attachments | **Ingest** | (already ingested or ingest if needed) |
| No attachments + **not** Gmail-filed to a project | **Skip** — no ACC folder, no `00_Email.pdf` | — |
| No attachments + **is** Gmail-filed to a project | **Ingest** (folder + best-effort body PDF) | **Ingest** if not yet on ACC |

Outcomes for skip: reuse `SkippedNoAttachments` / add clear status text, or `SkippedNotRelevant` — prefer a dedicated display string: “אין צרופות ולא משויך לפרויקט — לא מועלה ל-ACC”.

#### No redundant re-upload

1. If attachment / `00_Email.pdf` row already has `AccItemId` → **do not** re-render PDF and **do not** call AccService upload for that file.
2. Remove TEMP debug “force body PDF image refresh” (`BodyPdfImageRefreshDoneV2`) — one-shot re-upload on every process must not ship.
3. Keep **N4.1**: re-enter ingest only when body PDF is **missing** (`AccItemId` null) while message is `Uploaded` and renderer exists.
4. Keep H9 fallback (retry without `ExistingItemId` on HTTP 500) **only** on the first upload path for a missing file — never as a deliberate “refresh all” loop.
5. `AlreadyProcessed` short-circuit when folder + required files already have `AccItemId` remains the happy path.

#### In-scope code (after approval)

1. `EmailAccSelectionHandler.TryPassiveIngestAsync` / `CanUpload`: require `HasAttachments || IsFiledToProject` (mailbox filed).
2. `EmailListFilingCoordinator` (or post-File hook): after successful File-to-project, trigger ACC ingest when the message is not already terminal on ACC (covers zero-attachment filed emails).
3. `NativeEmailAccIngestionExecutor`: early skip when `attachments.Count == 0` and caller/command indicates not project-filed (defense in depth); restore body-PDF skip when `AccItemId` present; delete TEMP refresh dictionary.
4. Docs + gate tests; smoke: browse unfiled no-attachment → no ACC work; file to project → ACC appears; attachment email → ingest once, reselect → `AlreadyProcessed` / no second upload.

#### Risk & complexity

| Item | Assessment |
| --- | --- |
| Complexity | **Low–Medium** — gate + File hook + remove TEMP re-upload |
| AccService load | **Down** — main goal |
| Behavior drift from N4 soak | Intentional — unfiled empty emails no longer land in ACC on browse |
| DB/schema | **None** |

#### Approval

- Direction: operator 2026-07-31
- Code go-ahead: operator «כן לקוד N4.3» (2026-07-31)

### N4.2 — Embedded images in `00_Email.pdf` (2026-07-31)

**Problem:** PDF uploaded but inline/`cid:` images were blank — fixed ~400ms delay after navigation is not enough, and `cid:` sources are not loadable without a virtual-host handler (same as the UI body viewer).

**Fix:** `WpfEmailBodyPdfRenderer` rewrites `cid:` → `https://sinet-mail-images.local/…`, serves bytes via `WebResourceRequested`, then waits for `document.readyState === 'complete'` + all `img.complete && naturalWidth > 0` (≤15s) + 500ms settle before `PrintToPdfAsync` (parity with legacy `WebView2PdfRenderer`).

### N4.1 — Body PDF retry (2026-07-31)

**Problem:** First ingest often created `MSG_…` while WebView2 was cold → body PDF skipped non-fatally → later selects returned `AlreadyProcessed` from folder id alone → `00_Email.pdf` never retried.

**Fix:** `TryShortCircuitAlreadyProcessedAsync` must **not** short-circuit `Uploaded` when body attachment (`AttachmentIndex = -11`) has no `AccItemId` and `IEmailBodyPdfRenderer` is registered. Re-enter ingest to call `TryUploadBodyPdfAsync` again. Do not demote `Moved`.

**UI note (historical As-Is before FileMaterial six decisions):** `00_Email.pdf` lived in the message folder and was not taggable. **Target:** selectable as «תוכן המייל (PDF)» when the operator opts in — still not business material by default. Full rules: [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md).

---

## FileMaterial / MoveToProject — six decisions (pointer)

> **Canonical Target (full):** [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md)  
> **Date:** 2026-08-07  
> **Status:** Documentation source of truth for FileMaterial filing. Do not implement from this short summary alone.

### Existing State (As-Is gaps — historical)

| Decision | As-Is (pre–six decisions) |
| --- | --- |
| **1. AlreadyMovedToProject** | Truthy Move metadata → always failure — no compare to current ProjectFile / alt target. |
| **2. TotalCount + reconcile** | `TotalCount` only tagged rows with AccItemId (or ZIP); missing AccItemId excluded. |
| **3. FiledButMoveMetadataFailed** | Metadata write fail → warning only; still counted moved. |
| **4. Dismiss + workflow** | Dismiss on `AllFilesTransferred` even when Complete fails / advance pending. |
| **5. Email body PDF** | `00_Email.pdf` not taggable; empty-email auto-`Moved`. |
| **6. Direct email locate** | Correlation against **current Gmail page** only; subject/from fallback possible. |

### Target (six decisions) — summary only

See [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) §§1–15 for full flow, success/open conditions, TotalCount, reconcile, metadata fail, dismiss, body PDF, locate, dropped mechanisms, and tests.

### Out of Scope / Dropped

Documented in [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) §15 (no new retry/fallback/locator/pending table; no page scan; no subject/from pick; no re-upload after verify; no default body-as-business; no dismiss on AllFilesTransferred alone; no immediate deletion of inactive code).

---

## Slice N5 — ZIP/RAR archive extract + ZIP-level tagging (proposed)

### Problem (verified 2026-07-31 soak)

Gmail → ACC Native ingest uploads `.zip` as a single opaque file (no extract). Legacy + Jumbo extract ZIPs. RAR unsupported everywhere.

### Operator policy (target — 2026-07-31)

1. **Extract** `.zip` (and `.rar` when a supported library is approved) into `Attachments/{archiveName}/` for viewing.
2. **Tagging / Move unit = the archive** (one inbox attachment row for the ZIP/RAR), not each extracted child.
3. **Upload filter for extracted children:** business files only — **DWG, PDF, images** (e.g. png/jpg/jpeg/tif/tiff/bmp/webp). Skip fonts and other non-business types.
4. Optional: also keep the original archive file in ACC (TBD at code approval) — default proposal: **extract children + archive row pointing at folder** (Legacy parity via `AccVersionId` = zip folder id), without requiring a second tagged object.

### In scope — N5 (after code go-ahead)

1. Docs approval (this section + principles note).
2. Port Legacy ZIP extract behavior into `NativeEmailAccIngestionExecutor` (reuse/adapt Jumbo extract helpers where possible).
3. Tagging UI: one taggable chip for the archive; extracted files viewable/openable in ACC under the zip subfolder.
4. RAR: add only if an approved NuGet/library is chosen (else document ZIP-only and defer RAR).
5. Tests + soak with multi-file ZIP (DWG/PDF/image + noise files).

### Explicitly out of scope until approved

- Changing Legacy SiNetSQL behavior
- Perfect nested-archive / password-protected archives
- Tagging each extracted file separately

### Risk & complexity (pre-code)

| Item | Assessment |
| --- | --- |
| Complexity | **Medium–High** (extract + folder layout + tagging semantics + optional RAR) |
| Effort | Focused PR after library choice for RAR |
| Behavior drift | Aligns Native Gmail with Legacy ZIP; filters are **stricter** than Legacy (Legacy uploads all extracted files) |
| DB/schema | **None** expected |
| AccService load | Large ZIPs → many uploads; filter reduces volume |

### Approval notes (N5)

- Policy direction from operator (2026-07-31, option 3 with PDF-now)
- **Code starts only after explicit go-ahead on this N5 section** (and RAR library choice if RAR is in-scope)

