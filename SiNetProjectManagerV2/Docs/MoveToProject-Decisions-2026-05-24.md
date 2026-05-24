# MoveToProject — Business Decisions

**Decision date:** 2026-05-24
**Scope:** Move-to-Project flow from the Office Inbox into ACC.
**Rule:** This document is the source of truth for the MoveToProject business rules. Older docs that conflict must be marked `SUPERSEDED 2026-05-24` and link here.

---

## 1. OpenQuoteProject does NOT have to create an ACC project

OpenQuoteProject is responsible for:

- Creating the Project in the DB.
- Linking the email to the project.
- Adding the Gmail / project label if relevant.
- Closing the originating task with `ProjectOpened`.
- Advancing to `FileQuoteMaterial`.

It is **not** responsible for creating an ACC project, ACC folder tree or ACC permissions.
Some projects remain in quote-only state, so eager ACC provisioning is wasteful and may even fail for non-billable previews.

## 2. MoveToProject performs ACC ensure at move time — and only at move time

When (and only when) the user actually moves files, the system ensures the physical destination exists:

1. Verify the project exists in the DB.
2. Verify it has an ACC project / ACC context.
3. If no ACC project exists — create or link it via the **existing** provisioning service. **No parallel provisioning layer.**
4. Verify the specific target folder exists.
5. If it does not — create only that folder and its required parent path.
6. Then perform the upload.

The single approved entry point for this ensure sequence is **`ProjectFileFilingService.FileToAccAsync`**, which already calls:

- `IAccProjectProvisioningService.EnsureProjectMappingAsync(...)`
- `_accClient.EnsureFolderPathAsync(...)` for the target folder path only.

Do **not** introduce a new provisioning mechanism in the MoveToProject layer.

## 3. Only the required folders are created

- Do not pre-create the full ACC folder tree.
- Do not create empty folders.
- Do not create all 34 TagTargets.
- Create only the destination folder for the file being moved right now plus the parent folders needed to reach it.

## 4. Same-file check before re-upload (target state)

When a file with the same display name already exists in the target ACC folder, MoveToProject must compare the incoming email attachment against the existing ACC item using metadata, not name alone.

The comparison fields, when available, are:

- Original source file name.
- File size in bytes.
- Email date (default source-of-date for inbox attachments; an attachment-level reliable date may be used instead if it is documented and trusted).
- SHA / hash if available.
- Source identifier (attachment id / Gmail message id) if already persisted.

If a field is missing on the existing ACC item, the system reports it; it never silently fabricates a value.

**Status: IMPLEMENTED in continuation round 2026-05-24.** See section 12.

## 5. Identical file → do not re-upload

If every available identification field matches between the email attachment and the existing ACC item, the file is considered already filed.
The system must not upload it again, must not create a new version, and must not present the result as a fresh move.

## 6. Different file → upload as a new ACC version

If any identification field differs, the file is treated as a new version of the same logical document and uploaded via the existing ACC version mechanism (`UploadNewVersionAsync`).
Never create a duplicate file when a version is the correct semantic, and never silently overwrite.
If no version mechanism is available, stop and report — do not invent a parallel path.

## 7. Button is disabled when every attachment is already placed

If every taggable attachment in the selected email is already placed (`IsPlaced == true`):

- `MoveToProjectCommand.CanExecute` returns `false`.
- `MoveToProjectBlockReason` returns the Hebrew message: **"כל הקבצים כבר תויקו לפרויקט."**
- No additional move attempt is triggered.

If only some attachments are missing or differ, the button remains enabled and the move processes only what is needed.

## 8. UI result classification — `Moved 0/N` is never ✓

The view-model classifies a `MoveToProjectResult` (kind = `Completed`) into one of four buckets via `MoveToProjectStatusClassifier.Classify(...)`:

| Bucket            | Condition                                                                                                                                                              | Indicator |
|-------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| `Success`         | `MovedCount > 0 && MovedCount == TotalCount && FailedCount == 0`                                                                                                       | ✓ green   |
| `Partial`         | `0 < MovedCount < TotalCount`                                                                                                                                          | ⚠ warning |
| `AllAlreadyFiled` | `MovedCount == 0 && FailedCount == 0 && SkippedCount == TotalCount` **and** every per-attachment outcome is `SkippedAlreadyFiled` / `SkippedAlreadyPlacedHint` / `AlreadyMovedToProject` | neutral   |
| `Failure`         | Anything else where `MovedCount == 0` (failures, `MissingInAcc`, deferred-only, no outcomes, etc.)                                                                     | ✗ red     |

`FailedCount == 0` alone is no longer enough to display the green check. A `Skipped` outcome that is not a confirmed already-filed kind (for example `MissingInAcc`, `RequiresUi`, `Deferred`) is classified as `Failure`, not as neutral.

## 9. Task closure

`FileQuoteMaterial` closes only when MoveToProject actually succeeds for at least one inbox attachment.

The handler already enforces `filedInboxAttachmentIdsThisRun.Count > 0` before invoking the task completion coordinator. Closure is not triggered when:

- `MovedCount == 0` while files were expected to move.
- ACC provisioning fails.
- Folder creation fails.
- Upload / download fails.

Whether a fully `AllAlreadyFiled` run should close the task is **not** changed in this round. The current behavior is "do not close" because no inbox attachment was filed; if the business decides differently later, a dedicated round is required.

---

## Open items deferred to a future approved round

These were called out in the 2026-05-24 round but require either new ACC custom-attribute definitions (provisioned only via the existing Bim360 path) and/or schema/migration changes, both of which are gated by separate approval rules:

1. ~~**Same-file SHA / size / email-date comparison and the new `SiInbox.Source.*` attributes**~~ → **IMPLEMENTED 2026-05-24 (continuation round).** See section 12.
2. ~~**Promoting `EmailInboxAttachment.ContentSha256`** into the ACC metadata write path~~ → **IMPLEMENTED 2026-05-24 (continuation round).** `ContentSha256` is now part of the source identity written to and compared against `SiInbox.Source.ContentSha256`.
3. ~~**Policy for `AllAlreadyFiled` runs** with respect to closing `FileQuoteMaterial`~~ → **IMPLEMENTED 2026-05-24 (continuation round).** Verified same-source attachments now count toward the task-closure success condition (`failedCount == 0 && (movedCount + alreadySameSourceCount) > 0`). A run that proves every attachment is already filed therefore closes `FileQuoteMaterial`. Legacy items lacking source metadata still do **not** close the task.

Older guidance stating "the task is not closed when all attachments are already filed" is **SUPERSEDED 2026-05-24** by section 12 below.

---

## 12. SiInbox.Source.* — same-source identity and verified AllAlreadyFiled (continuation round 2026-05-24)

### 12.1 Identity attributes

Six ACC custom attributes provisioned through the existing path
(`AccBootstrapService.EnsureInboxCustomAttributeDefinitionsAsync` →
`Bim360Service.EnsureCustomAttributeDefinitionsAsync`) describe the original
inbox source of every ACC item produced by MoveToProject:

| Attribute | Source |
|-----------|--------|
| `SiInbox.Source.GmailMessageId` | `EmailInboxMessage.MessageUniqueId` |
| `SiInbox.Source.MessageDateUtc` | `EmailInboxMessage.ReceivedUtc` (canonical source date — **not** `SiLastSavedUtc`) |
| `SiInbox.Source.OriginalFileName` | `EmailInboxAttachment.OriginalFileName` |
| `SiInbox.Source.FileSizeBytes` | attachment file size in bytes |
| `SiInbox.Source.ContentSha256` | `EmailInboxAttachment.ContentSha256` when available |
| `SiInbox.Source.AttachmentId` | `EmailInboxAttachment.Id` |

These names are centralized in `SidecarMetadata.InboxAccAttributeNames` and are the **only** identity attributes used; no parallel mechanism is introduced.

### 12.2 Write path (best-effort)

After every successful first upload or new-version upload, `ProjectFileFilingService.FileToAccAsync` writes the populated `SiInbox.Source.*` attributes through `IAccItemMetadataService.WriteAttributesAsync`. Metadata write failures are logged as warnings and **must not** be treated as upload failures or as `MissingInAcc`.

### 12.3 Compare path (conservative)

When a same-name item already exists in the target ACC folder, the service reads its current `SiInbox.Source.*` values via `IAccItemMetadataService.ReadAttributesAsync` and compares them against the incoming request:

- **`ContentSha256` is decisive** when present on both sides — equal SHA proves same-source.
- Otherwise, sameness requires a **strong identifier** (`GmailMessageId` or `AttachmentId`) to be equal **and** all available `MessageDateUtc`, `FileSizeBytes`, `OriginalFileName` to also be equal (no missing-side comparisons accepted).
- Legacy items with **no** `SiInbox.Source.*` metadata are **never** treated as same-source; they fall through to the existing `UploadNewVersionAsync` path.

When sameness is proven, the service returns `FileProjectFileResult.AlreadySameSource = true`, performs **no** upload and **no** new ACC version, and does **not** rewrite the existing item's source metadata.

### 12.4 MoveToProject outcome and counts

`MoveToProjectProcessActionHandler` translates a same-source result into the new outcome kind `MoveToProjectAttachmentOutcomeKind.AlreadyFiledSameSource` and tracks `alreadySameSourceCount` separately from `movedCount`. `EmailMoveToProjectApplicationService` adds `alreadySameSourceCount` into the returned `SkippedCount` so the existing `MoveToProjectStatusClassifier` can resolve fully verified runs to `AllAlreadyFiled`.

### 12.5 UI and task closure

- `Moved 0/N ✓` is still never displayed (section 8 unchanged).
- A run where every attachment is `AlreadyFiledSameSource` (or any other `AlreadyFiled` variant) resolves to `AllAlreadyFiled` and is shown neutrally as "כל הקבצים כבר תויקו לפרויקט. המשימה הושלמה." (or equivalent).
- `EmailMarkedMoved` and the `FileQuoteMaterial` task closure now fire when `failedCount == 0 && (movedCount + alreadySameSourceCount) > 0`, i.e. verified same-source attachments contribute to closure exactly like fresh moves. This supersedes the deferred wording in section 9.

### 12.6 Non-goals (explicit)

- **No** schema, migration, or `ModelSnapshot` changes were made.
- **No** new provisioning layer was introduced; the existing `AccBootstrapService` / `Bim360Service` ensure path is reused.
- **No** auto-sync, pre-creation of the full ACC tree, or empty-folder provisioning.
- **No** filename-only sameness proof; legacy items remain conservative.

---

## What changed in code in this round (2026-05-24)

- `SiNetSQL/Services/MoveToProject/MoveToProjectContracts.cs` — added `MoveToProjectStatusKind` and the pure `MoveToProjectStatusClassifier.Classify(...)` helper.
- `SiNetSQL/MVVM/EmailManagementViewModel.cs` — `Completed` branch now routes through the classifier; Hebrew status text differs per bucket and `Moved 0/N` never shows ✓. `MoveToProjectBlockReason` disables the button with "כל הקבצים כבר תויקו לפרויקט." when every taggable attachment is already placed.
- `SiNetSQL.Tests/Services/MoveToProject/MoveToProjectStatusClassifierTests.cs` — new test file covering Success / Failure / Partial / AllAlreadyFiled / null guard.

No schema, migration, ModelSnapshot, `ProjectFileInstance`, refile, `MoveToProject` service architecture, `SetItemCustomAttributesAsync`, TokenProvider, or `Bim360Service` changes were made.

---

## What changed in code in the continuation round (2026-05-24)

Implements the previously deferred items 1–3 without any schema/migration/`ModelSnapshot` change and without introducing a parallel provisioning layer.

- `SiNetSQL/FileIndex/SidecarMetadata.cs` — added the six `SiInbox.Source.*` constants under `InboxAccAttributeNames`.
- `SiNetSQL/Services/AccBootstrap/AccBootstrapService.cs` — `BuildInboxCustomAttributeDefinitions()` now also returns the source-identity definitions; provisioning continues to go through the existing `EnsureInboxCustomAttributeDefinitionsAsync` → `Bim360Service.EnsureCustomAttributeDefinitionsAsync` path.
- `SiNetSQL/Services/Files/IProjectFileFilingService.cs` — extended `FileProjectFileRequest` with the six optional source-identity init properties; added `AlreadySameSource` to `FileProjectFileResult`.
- `SiNetSQL/Services/Files/ProjectFileFilingService.cs` — `FileToAccAsync` now reads `SiInbox.Source.*` on same-name items, short-circuits with `AlreadySameSource = true` when identity matches (SHA-decisive; otherwise strong-id + size/date/name agreement; legacy missing metadata never proves sameness), and writes `SiInbox.Source.*` best-effort after upload/version (metadata failures are logged warnings only).
- `SiNetSQL/Services/MoveToProject/MoveToProjectContracts.cs` — added `MoveToProjectAttachmentOutcomeKind.AlreadyFiledSameSource`; the classifier treats it as an already-filed variant alongside `SkippedAlreadyFiled`, `SkippedAlreadyPlacedHint`, and `AlreadyMovedToProject`.
- `SiNetSQL/Domain/Actions/Handlers/MoveToProjectProcessActionHandler.cs` — populates the six source-identity fields on every filing request from the inbox message/attachment, distinguishes the `AlreadyFiledSameSource` outcome, and tracks `alreadySameSourceCount` (exposed via the result data dictionary). The success/closure condition is now `failedCount == 0 && (movedCount + alreadySameSourceCount) > 0`.
- `SiNetSQL/Services/MoveToProject/EmailMoveToProjectApplicationService.cs` — maps the new handler outcome name; adds `AlreadySameSourceCount` into the returned `SkippedCount` so verified runs reach `AllAlreadyFiled`.
- `SiNetSQL.Tests/Files/ProjectFileFilingService_SameSourceTests.cs` — new test file covering same-source no-upload, same-name different-source new-version, legacy-no-metadata new-version, source-attribute write payload, and metadata-write-failure best-effort behavior.
- `SiNetSQL.Tests/Services/MoveToProject/MoveToProjectStatusClassifierTests.cs` — added cases for `AlreadyFiledSameSource` (alone and mixed with the other already-filed variants).

No schema, migration, ModelSnapshot, `ProjectFileInstance`, refile, `MoveToProject` service architecture, `SetItemCustomAttributesAsync`, TokenProvider, or `Bim360Service` changes were made.
