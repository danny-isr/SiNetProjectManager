# FileMaterial / MoveToProject — Canonical behavior (six decisions)

> **Status:** Target (documentation source of truth) — **2026-08-07**  
> **Audience:** Agents and developers implementing `PRP.FileMaterial` / `FileQuoteMaterial` filing via Email MoveToProject  
> **Host:** `SiNet.App.Wpf` (production). V2 Legacy handler is reference only.  
> **Related SoT:** [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) · ingest slices [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) · workflow stage map [`manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md)  
> **Missing historical log:** V2 `MoveToProject-Decisions-2026-05-24.md` is **not in repo**; this document is the interim + current Target for FileMaterial six decisions.

---

## 0. Purpose of this document

This file is the **single source of truth** for how FileMaterial filing must behave after the six operator decisions (2026-08).  
Implementation must match this document section-by-section. Behavior changes require updating this document **first**.

**Documentation-first halt (2026-08-07):** Code changes from an earlier implementation pass exist in the working tree and must **not** be treated as complete until they are compared to this document and corrected. See §16.

---

## 1. Process purpose and ownership

### 1.1 FileMaterial (`PRP.FileMaterial` / task type `FileQuoteMaterial`)

**Purpose:** Collect and **file required quote material** from the task’s email into the project file tree (via ACC Inbox → `IProjectFileFilingService` / MoveToProject).

**Owns:**
- Opening the Email work surface for the task’s `EmailInboxMessage` work target
- Ensuring the correct Gmail message is selected (exact id)
- Tagging attachments to `ProjectFile` / alternative
- Running MoveToProject until every **required** item is filed **or verified** at the current target with Move/Lock metadata complete
- Calling `ITaskCompletionService.CompleteAsync` so the stage can advance (typically via `AllRequiredTasksClosed` → `PRP.MaterialCheck`)
- Dismissing the filing window only under §10

**Does not own:**
- Deciding whether material is *sufficient for calculation* (that is MaterialCheck)
- Creating a parallel filing pipeline outside `NativeEmailMoveToProjectExecutor` / existing coordinators
- Mailbox “משויך” proof (Gmail project label remains SoT — see EMAIL_ACC SoT)

### 1.2 MaterialCheck (`PRP.MaterialCheck` / `CheckQuoteMaterialCompleteness`)

**Purpose:** Operator judgment — is the filed material complete enough to continue?

| | FileMaterial | MaterialCheck |
| --- | --- | --- |
| Question | Were required files transferred/verified into project storage? | Is the set of materials *enough* for the next stage? |
| Surface | Email (tag + Move) | Typically ProjectWork / checklist |
| Success event | Task close after full transfer + `CompleteAsync` → stage advance | `MaterialComplete` / `MaterialMissing` (self-loop) |
| File transfer | Yes (MoveToProject) | No (review / result codes) |

FileMaterial **feeds** MaterialCheck; it does **not** replace it.

---

## 2. End-to-end flow (Target)

```text
Task open (FileQuoteMaterial)
  → WorkSurfaceContext (ProjectId, TaskId, PrimaryWorkTargetEntityId = EmailInboxMessageId,
                        CompletionEventCode)
  → EmailWindowViewModel.ApplyTaskContext
       1. Load SQL inbox row (IEmailInboxQuery.GetByIdAsync)
       2. Register PendingTaskSelection (exact MessageUniqueId / InternetMessageId / InboxMessageId)
       3. Set project context + refresh mailbox page (existing paging)
       4. Direct locate (§12): GetByIdAsync(gmail id) OR AllMail rfc822msgid:{InternetMessageId}
          → EmailListRowMapper → inject/select; patch InboxMessageId
          Fail closed if not found / Gmail error (task + window stay open; no alternate email)
  → Operator: File-to-project (Gmail label) if needed; tag attachments; optional body PDF
  → MoveToProject (IEmailMoveToProjectService → NativeEmailMoveToProjectExecutor)
       reconcile/recover (§7) → process required items (§5–§8)
  → If AllFilesTransferred (§6):
       UI CompleteAsync (§9)
       If TaskClosed && WorkflowAdvanced to MaterialCheck (not WorkflowAdvancePending):
            WorkItemDismissRequested / shell dismiss (§10)
  → Workflow stage → PRP.MaterialCheck (engine / existing advance path)
```

---

## 3. Exact success conditions (FileMaterial filing run)

A Move+Complete cycle is a **full success** only when **all** are true:

1. **Required items** (§5) count `TotalCount > 0` **or** explicit «אין חומר» path (§11) was confirmed and completed.
2. For every required item: filed in this run **or** verified already at the **current** target (§8) **and** Move/Lock metadata is complete (not `FiledButMoveMetadataFailed`).
3. `FailedCount == 0`.
4. `AllFilesTransferred == true` (§6).
5. Inbox message status may become `Moved` only when (2)–(4) hold.
6. `ITaskCompletionService.CompleteAsync` returns `Success == true`.
7. `TaskClosed == true`.
8. Workflow advance to MaterialCheck completed: `WorkflowAdvanced == true` and `WorkflowAdvancePending == false` (see §9).
9. Only then may the UI raise `WorkItemDismissRequested` / dismiss the filing surface (§10).

---

## 4. Conditions where task and window stay open

Keep **task open** and **filing window open** when any of:

| Situation | Notes |
| --- | --- |
| Required item missing AccItemId after reconcile/recover | `MissingAccItemId` / `MissingInAcc` |
| External download missing (Jumbo/WeTransfer) | No Gmail recover; stay on external download path |
| `AlreadyMovedConflict` | Moved metadata points at a **different** target |
| `Locked` without verified same-target Move | Cannot file |
| `FiledButMoveMetadataFailed` | Physical OK; metadata incomplete |
| Partial `FailedCount > 0` | Any filing/download/tag failure |
| `TotalCount == 0` without «אין חומר» confirmation | Deferred — operator must tag or confirm |
| Direct locate failed / Gmail down | No alternate email selection |
| `CompleteAsync` failed (`Success == false`) | Files may already be OK; retry Complete later |
| `WorkflowAdvancePending == true` | Task may be closed; advance incomplete — **do not dismiss** |
| `TaskClosed == false` after Complete | Do not dismiss |
| Operator chose «חזרה» on empty-attachments dialog | Abort Move |

---

## 5. Required items, TotalCount, AllFilesTransferred

### 5.1 What counts as a required business item

An inbox attachment is **required** when:

- It has a `ProjectFileId` tag (operator tagged a destination), **and**
- It is **not** `manifest.json`, **and**
- It is **not** another system/inline row (`AttachmentIndex < 0`) **except** the email body PDF when tagged (below).

**Email body (`00_Email.pdf`, `AttachmentIndex = -11`):**
- **Not** required by default.
- Becomes required **only if** the operator tags it («תוכן המייל (PDF)»).
- Never auto-classified as business material without that tag.

**ZIP folder rows** (AccVersionId, no AccItemId, `.zip`): still required when tagged; processed via existing ZIP folder path.

### 5.2 TotalCount

```text
TotalCount = count(required items)
```

Includes required items **even when `AccItemId` is still null**.  
Missing AccItemId does **not** shrink TotalCount; it contributes to failure / incomplete until recovered or filed.

### 5.3 AllFilesTransferred

Canonical definition (Application result types):

```text
AllFilesTransferred =
  Outcome succeeded
  && FailedCount == 0
  && TotalCount > 0
  && (MovedCount + AlreadySameSourceCount) >= TotalCount
```

`AlreadySameSourceCount` includes:
- Filing service `AlreadySameSource`, **and**
- ACC Move metadata verified against the **current** tag target (§8) without re-upload.

`FiledButMoveMetadataFailed` increments `FailedCount` and **must not** increment `MovedCount` / `AlreadySameSourceCount` as a full completion.

---

## 6. Attachment outcome vocabulary (Kinds)

| Kind | Meaning | Counts as success? |
| --- | --- | --- |
| *(success moved)* | New file filed + Move/Lock written | Yes → `MovedCount` |
| *(verified same)* | Already at current target (filing or Move metadata match) | Yes → `AlreadySameSourceCount` |
| `AlreadyMovedConflict` | Move metadata present but target ≠ current tag | No |
| `FiledButMoveMetadataFailed` | Physical file OK; Move/Lock write failed | No (incomplete) |
| `MissingAccItemId` | Required, still no AccItemId after recover attempt | No |
| `MissingInAcc` | Not found in ACC Inbox folder | No |
| `Locked` | Lock without verified same-target Move | No |
| `DownloadFailed` / `FilingFailed` / `ZipFilingFailed` / `NoFilingTag` | As named | No |

User-visible Hebrew strings: `EmailMoveToProjectOutcomeDisplay`.

---

## 7. Reconciliation vs Gmail and ACC

**Before** treating a Move run as able to complete:

1. **ACC reconcile** via existing `IEmailAccStatusService` / `IAccInboxReconciliationService` (read + presence). DB `AccItemId` is cache only.
2. **Gmail recovery** for required rows that are **not** `IsExternalDownload` and lack AccItemId (and are not ZIP-folder rows): existing `IEmailAccRecoveryExecutor.RecoverMissingAttachmentsAsync` → re-ingest via N1.
3. **External (Jumbo / WeTransfer / `IsExternalDownload`):** **do not** Gmail-recover. Leave on existing external download / re-download UI path. Clearing AccItemId + Gmail ingest must not wipe link-only files.
4. Ready files may be filed in the same run; any still-missing required item keeps `FailedCount > 0` and the task open.

No new reconcile service. No new automatic retry loop beyond this existing recover call at the start of Move.

---

## 8. File already at destination (AlreadyMoved / same source)

When ACC attributes include truthy `MoveMovedToProject`:

1. Read `MoveTargetProjectId`, `MoveTargetProjectFileId`, `MoveTargetProjectAlternativeId` (same names written by `WriteMoveLockMetadataAsync`).
2. Compare to current filing tag (`projectId` + tagged ProjectFile / alt).
3. **Match** → count `AlreadySameSourceCount`; **do not** download/re-upload.
4. **Mismatch or incomplete target attributes** → `AlreadyMovedConflict`; task stays open.
5. Prefer checking Move metadata **before** treating Lock alone as a hard failure when Move is present (Lock is expected after a successful Move/Lock write).

When Move metadata is absent but `FileAsync` returns `AlreadySameSource`: treat as same-source success **only after** Move/Lock metadata write succeeds. If metadata write fails → §9 / Kind `FiledButMoveMetadataFailed`.

---

## 9. Move/Lock metadata failure (`FiledButMoveMetadataFailed`)

When physical filing succeeds (`FileAsync` / ZIP children) but `WriteMoveLockMetadataAsync` fails:

| Must | Must not |
| --- | --- |
| Record Kind `FiledButMoveMetadataFailed` | Count as full `MovedCount` success |
| Increment `FailedCount` | Set inbox `Moved` |
| Keep `AllFilesTransferred == false` | Call executor task-completion / emit MaterialFiled from incomplete run |
| Show: physical OK, metadata incomplete | Treat `warningCount`-only as success |

**Operator retry (same Move command, no new retry stack):**  
If the destination is already verified / `AlreadySameSource`, **retry metadata write only** — **no re-upload** of file bytes. Download-to-temp for a same-source `FileAsync` probe is an implementation detail; content must not be uploaded again.

**INACTIVE (do not restore):** treating metadata failure as `warningCount++` while still counting the item moved.

---

## 10. Task completion, workflow advance, window dismiss

### 10.1 Who closes the task

When `TaskId` is present on the filing surface, **UI owns** completion:

- `EmailDetailViewModel` → `ITaskCompletionService.CompleteAsync` after `AllFilesTransferred`.
- Executor `ReportTaskCompletionAsync` is **inactive** for the TaskId path (retained for reference / non-UI tooling only). Avoid double Complete.

### 10.2 CompleteAsync outcomes

| Result | UI behavior |
| --- | --- |
| `Success == false` | Message; window stays open; **no** dismiss |
| `Success` + `WorkflowAdvancePending` | Message: task may be closed but advance pending; use existing `IWorkflowRecoveryService` / `StalledWorkflowWatchdog`; **no** dismiss; **not** full success |
| `Success` + `TaskClosed` + advance done (`WorkflowAdvanced`, not pending) | May dismiss (§10.3) |
| `Success` but `TaskClosed == false` | Message; **no** dismiss |

### 10.3 WorkItemDismissRequested / TryDismissFilingSurface

Fire dismiss **only** when:

1. `AllFilesTransferred` is true for the Move result, **and**
2. `CompleteAsync` succeeded, **and**
3. `TaskClosed == true`, **and**
4. Workflow is **not** `WorkflowAdvancePending` (advance toward MaterialCheck completed for this completion).

**INACTIVE:** dismiss on `AllFilesTransferred` alone (even if Complete failed).

Manual Move without `TaskId`: no work-item dismiss.

---

## 11. Email with no business attachments / body PDF

### 11.1 Selectable «תוכן המייל (PDF)»

- Represents existing ACC layout file `00_Email.pdf` (`AccInboxLayout.EmailBodyFileName` + `WpfEmailBodyPdfRenderer`).
- **Not** a second PDF generator.
- Shown as taggable in the attachment strip when the body attachment row exists.
- Default: **out** of TotalCount until tagged.
- When tagged: same Move/Lock rules as any required item.

### 11.2 Empty business-attachments dialog (task-driven)

When there are **no** business (non-body) attachments to file and a filing `TaskId` is active:

| Choice | Behavior |
| --- | --- |
| **Yes** — include body PDF | Operator must tag «תוכן המייל (PDF)» then Move; if body missing/un-tagged, abort with clear message |
| **No** — confirm no material | Explicit confirmation; then `CompleteAsync` → MaterialCheck path without Move; log/status via existing completion (no new table) |
| **Back / Cancel** | Abort; task and window stay open |

**INACTIVE:** empty-email shortcut that auto-sets `EmailInboxStatus.Moved` and returns Succeeded without operator choice.

---

## 12. Direct email locate (task open)

After SQL inbox load and mailbox refresh:

1. If Gmail `MessageUniqueId` present → `IEmailGateway.GetByIdAsync`.
2. Else / if null → `GetMailboxPageAsync` with `MailboxScope.AllMail` and `FreeText = rfc822msgid:{InternetMessageId}` (same pattern as OpenQuote / QuoteSend).
3. Map with `EmailListRowMapper`; set `EmailInboxMessageId`; select via existing detail pipeline.
4. `PendingTaskSelection` must survive late `ReplaceRows` without selecting a subject+from twin or the first page row.
5. When message ids are known, **do not** fall back to subject/from matching for task selection.

**Fail closed:** not found / Gmail error → clear status; task + window open; **no** substitute email; **no** MaterialCheck advance from this failure.

**Dropped:** Gmail page scanning; parallel locator service; subject+from alternate pick; pending DB table.

---

## 13. User messages and error states (minimum)

| Situation | Intent of message |
| --- | --- |
| Partial Move | X/Y filed; list failure Kinds; task not closed |
| AlreadyMovedConflict | Already filed to a **different** ACC target |
| FiledButMoveMetadataFailed | Physical OK; metadata incomplete; retry Move |
| MissingAccItemId / MissingInAcc | Need upload/recover (Gmail) or external re-download |
| Locate failed | Exact id not found; no alternate |
| Complete failed | Files OK but task completion failed; window stays |
| WorkflowAdvancePending | Task closed / files OK; advance waiting; window stays |
| Empty attachments | Yes / No / Back copy as §11 |
| No tags | Deferred — tag destinations first |

Hebrew formatting: `EmailMoveToProjectOutcomeDisplay` (+ dialogs in `EmailDetailViewModel`).

---

## 14. Mechanisms to reuse / extend (only)

| Area | Mechanism |
| --- | --- |
| Move backend | `NativeEmailMoveToProjectExecutor`, `IEmailMoveToProjectCoordinator` / Service |
| Filing | `IProjectFileFilingService` |
| ACC IO / metadata | `IAccFileDownloadService`, `IAccFileUploadService`, `IAccFolderBrowserService`, `IAccItemMetadataService`, `SidecarMetadata.InboxAccAttributeNames` |
| Reconcile / recover | `IEmailAccStatusService`, `IAccInboxReconciliationService`, `IEmailAccRecoveryExecutor` |
| Task close | `ITaskCompletionService` / `TaskCompletionResultDto` |
| Workflow recovery | `IWorkflowRecoveryService`, `StalledWorkflowWatchdog` (display/ops — no new retry) |
| List / selection | `PendingTaskSelection`, `EmailListRowMapper`, `IEmailGateway` |
| Body PDF | `AccInboxLayout`, `WpfEmailBodyPdfRenderer`, tagging eligibility |
| External links | Existing Jumbo/WeTransfer browser + `IEmailExternalDownloadExecutor` |

---

## 15. Explicitly dropped / inactive / deferred

| Decision | Status |
| --- | --- |
| New automatic retry stack | **Dropped** |
| New fallback path | **Dropped** |
| Parallel email locator service | **Dropped** |
| New pending DB table | **Dropped** |
| Scan Gmail pages for the task email | **Dropped** |
| Select email by subject + sender | **Dropped** (for task target when ids exist) |
| Re-upload after verified same target | **Dropped** |
| Auto-classify `00_Email.pdf` as required business | **Dropped** |
| Dismiss on `AllFilesTransferred` alone | **Inactive** (must not return) |
| Empty-email auto-`Moved` success | **Inactive** |
| Metadata fail = warning only | **Inactive** |
| Executor `ReportTaskCompletionAsync` when UI has TaskId | **Inactive** for that path |
| Immediate deletion of inactive code | **Deferred** — mark with reason; delete only after soak |

---

## 16. Implementation status vs this document (honest ledger)

> Code was changed in a prior agent pass **before** this full documentation. That pass is **not** declared complete.

### 16.1 Areas already touched in code (pending alignment audit)

- `NativeEmailMoveToProjectExecutor` — AlreadyMoved verify, required-item TotalCount, reconcile/recover hook, FiledButMoveMetadataFailed, skip executor Complete when TaskId
- `EmailDetailViewModel` — dismiss gating, empty-attachments dialog, Complete / WorkflowAdvancePending messaging
- Tagging eligibility — allow `00_Email.pdf` body index
- `EmailWindowViewModel` / list coordinators — GetById / rfc822msgid locate, PendingTaskSelection harden
- Outcome display Kind strings + source-guard tests

### 16.2 Required next step (after docs approval)

1. Diff each § of this document against the working tree.
2. Fix any gap (e.g. metadata-only retry without re-upload guarantees; dismiss requiring confirmed advance to MaterialCheck; product copy).
3. Update this §16 when alignment is verified.
4. Only then: build + tests + claim complete.

**Do not delete** the existing code wholesale; mark mismatches and correct toward this Target.

---

## 17. Required tests (by path)

| Path | Assert |
| --- | --- |
| AlreadyMoved same target | `AlreadySameSourceCount`; no re-upload |
| AlreadyMoved different target | `AlreadyMovedConflict`; task open |
| Required without AccItemId | Included in TotalCount; failure or recover then file |
| Ready + missing mix | Partial file; not AllFilesTransferred |
| Metadata fail | Kind set; AllFilesTransferred false; no Moved; no dismiss |
| Retry after metadata fail | Metadata write; no second content upload |
| CompleteAsync fail | No `WorkItemDismissRequested` |
| WorkflowAdvancePending | No dismiss; clear message |
| Full success | TaskClosed + advance; dismiss |
| No business attachments | Yes/No/Back behaviors |
| Body PDF tagged | In TotalCount; Move/Lock required |
| Locate on other page / archive | Inject + select by id |
| Locate not found / Gmail fail | Fail closed |
| Twin subject+from | Exact id wins; no wrong twin |
| External missing | No Gmail recover |

---

## 18. Six decisions — documentation map

| # | Decision | Sections |
| --- | --- | --- |
| 1 | AlreadyMoved verify target | §6, §8 |
| 2 | TotalCount + reconcile | §5, §7 |
| 3 | FiledButMoveMetadataFailed | §6, §9 |
| 4 | Dismiss only after Complete + MaterialCheck advance | §3, §4, §10 |
| 5 | Selectable 00_Email.pdf | §5.1, §11 |
| 6 | Direct email locate | §2, §12 |

---

## 19. Open questions / Needs Review

1. Exact Hebrew copy for «אין חומר» and `WorkflowAdvancePending` may be tuned after soak (behavior fixed).
2. Whether `WorkflowAdvanced` alone is sufficient proof of MaterialCheck stage, or UI must also assert `CurrentStage == PRP.MaterialCheck` from a query — **prefer existing CompleteAsync/StageAdvanceResult fields**; do not add a parallel stage poller unless documented here first.
3. Metadata-only retry without any temp download: preferred; if filing API requires a local path for AlreadySameSource check, document the probe as non-upload (still no re-upload).

---

## 20. Change log

| Date | Change |
| --- | --- |
| 2026-08-07 | Initial full Target document (Documentation First halt). Supersedes the short bullet list previously only in `NATIVE_EMAIL_ACC_INGEST.md` as the detailed SoT. |
