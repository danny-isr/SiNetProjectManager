# Email Management View and Actions Tree

- **Date:** 21.06.2026
- **Status:** Active
- **Scope:** EmailManagementView, EmailContextPanel, EmailViewerControl, EmailListControl, email action system, suggested actions builder, action executor, Gmail sync integration, attachment handling

---

## 1. Purpose

Document the Email Management screen and the context-aware actions system as currently implemented. This document is the source of truth for the current Email UI behavior, the action suggestion pipeline, action execution, and the current state of action availability.

---

## 2. Relevant screens and files

### Main UI files

| Component | File | Purpose |
|---|---|---|
| EmailManagementView (XAML) | `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml` (259 lines) | Main email tab — list, viewer, actions, calendar |
| EmailManagementView (code-behind) | `SiNetProjectManagerV2\WPFUserControl\EmailManagementView.xaml.cs` (938 lines) | Event routing, follow-up handling, continuation dialog dispatch |
| EmailContextPanel (XAML) | `SiNetProjectManagerV2\WPFUserControl\EmailContextPanel.xaml` (158 lines) | Context chips + action ComboBox |
| EmailContextPanel (code-behind) | `SiNetProjectManagerV2\WPFUserControl\EmailContextPanel.xaml.cs` (17 lines) | Minimal |
| EmailViewerControl (XAML) | `SiNetProjectManagerV2\Controls\EmailViewerControl.xaml` (353 lines) | Email header + attachments bar + WebView2 body |
| EmailViewerControl (code-behind) | `SiNetProjectManagerV2\Controls\EmailViewerControl.xaml.cs` (170 lines) | Encoding switch, attachment events |
| EmailListControl (XAML) | `SiNetProjectManagerV2\Controls\EmailListControl.xaml` (435 lines) | Email list with grouping and context menus |
| EmailListControl (code-behind) | `SiNetProjectManagerV2\Controls\EmailListControl.xaml.cs` | Minimal |
| EmailActionBarControl (XAML) | `SiNetProjectManagerV2\Controls\EmailActionBarControl.xaml` (98 lines) | Filing status + file/move-to-project button |
| EmailPreviewWindow | `SiNetProjectManagerV2\Dialogs\EmailPreviewWindow.xaml.cs` (146 lines) | Floating email preview for project creation flows |

### ViewModel files

| ViewModel | File | Purpose |
|---|---|---|
| EmailManagementViewModel | `SiNetSQL\MVVM\EmailManagementViewModel.cs` (~6,100 lines, ~340KB) | Main email tab logic — Gmail OAuth, loading, filing, tagging, ACC upload |
| EmailContextViewModel | `SiNetSQL\MVVM\EmailContextViewModel.cs` (1629 lines) | Context analysis → suggested actions → action execution |
| EmailViewerViewModel | `SiNetSQL\MVVM\Components\EmailViewerViewModel.cs` (192 lines) | Single email display |

### Action system files (SiNetSQL)

| Component | File | Purpose |
|---|---|---|
| EmailContextAnalyzer | `SiNetSQL\Services\EmailContext\EmailContextAnalyzer.cs` (334 lines) | Analyzes email → builds `EmailContextResult` |
| SuggestedActionsBuilder | `SiNetSQL\Services\EmailContext\SuggestedActionsBuilder.cs` (448 lines) | Rule engine: context → prioritized action list |
| ActionExecutor | `SiNetSQL\Services\EmailContext\ActionExecutor.cs` (1758 lines) | Dispatches selected action to domain services |
| SuggestedActionType (enum) | `SiNetSQL\DTOs\Email\SuggestedActionType.cs` (161 lines) | 43 action types in ranges |
| SuggestedAction (DTO) | `SiNetSQL\DTOs\Email\SuggestedAction.cs` (35 lines) | Action type + display + confidence |
| ActionResultStatus | `SiNetSQL\Services\EmailContext\ActionResultStatus.cs` (19 lines) | Completed / RequiresFollowUp / Failed / NotSupported |
| EmailWorkflowStateEvaluator | `SiNetSQL\MVVM\Coordinators\EmailWorkflowStateEvaluator.cs` (174 lines) | Email filing state machine |
| ActionDefinitionRegistry | `SiNetSQL\Domain\Actions\ActionDefinitionRegistry.cs` (33KB) | Complete registry of all action definitions |
| ActionCodes | `SiNetSQL\Domain\Actions\ActionCodes.cs` (8KB) | Stable machine codes for all actions |

### Domain action handlers

| Handler | File | Purpose |
|---|---|---|
| LinkToProjectHandler | `SiNetSQL\Domain\Actions\Handlers\LinkToProjectProcessActionHandler.cs` (7KB) | Link email to project |
| MoveToProjectHandler | `SiNetSQL\Domain\Actions\Handlers\MoveToProjectProcessActionHandler.cs` (67KB) | Full move-to-project pipeline |
| AddMaterialHandler | `SiNetSQL\Domain\Actions\Handlers\AddMaterialToProjectProcessActionHandler.cs` (7KB) | Add attachments to project |
| StartWorkflowHandler | `SiNetSQL\Domain\Actions\Handlers\StartWorkflowProcessActionHandler.cs` (9KB) | Start workflow from email |
| WorkflowTransitionHandlers | `SiNetSQL\Domain\Actions\Handlers\WorkflowTransitionProcessActionHandlers.cs` (23KB) | Advance workflow from email |
| CreateTaskHandler | `SiNetSQL\Domain\Actions\Handlers\CreateTaskProcessActionHandler.cs` (4KB) | Create task from email |
| FileOnlyHandler | `SiNetSQL\Domain\Actions\Handlers\FileOnlyProcessActionHandler.cs` (2KB) | File email without further action |
| ApproveOrCloseHandler | `SiNetSQL\Domain\Actions\Handlers\ApproveOrCloseProcessActionHandler.cs` (8KB) | Approve or close project |
| CloseOpinionHandler | `SiNetSQL\Domain\Actions\Handlers\CloseOpinionProcessActionHandler.cs` (7KB) | Close opinion project |

### Action continuation system

| Component | File | Purpose |
|---|---|---|
| Continuation interfaces | `SiNetSQL\Domain\Actions\Continuation\IActionContinuation.cs` | Request/Result/UiKind abstractions |
| WPF continuation host | `SiNetProjectManagerV2\Services\WpfActionContinuationUiHost.cs` (703 lines) | Routes continuation requests to WPF dialogs |
| Continuation types (6) | `SiNetSQL\Domain\Actions\Continuation\*.cs` (15 files) | WorkflowAdvance, TaskCreation, FileImport, ProjectPicker, NewProject, Decision, Discipline |

### Email services

| Service | File | Purpose |
|---|---|---|
| EmailFilingService | `SiNetSQL\Services\Email\EmailFilingService.cs` (410 lines) | Single source of truth for filing/unfiling |
| EmailManagementService | `SiNetSQL\Services\Email\EmailManagementService.cs` (15KB) | Email management operations |
| EmailAttachmentService | `SiNetSQL\Services\Email\EmailAttachmentService.cs` (9KB) | Attachment CRUD |
| EmailIngestionService | `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs` (98KB) | Email upload to ACC |
| AttachmentTaggingService | `SiNetSQL\Services\EmailIngestion\AttachmentTaggingService.cs` (33KB) | Attachment tagging logic |
| EmailContextService | `SiNetSQL\Services\EmailContextService.cs` (812 lines) | Thread color/status mapping |

### Model files

| Model | File | Key fields |
|---|---|---|
| EmailInboxMessage | `SiNetSQL\Models\EmailInboxMessage.cs` (214 lines) | Id, MessageUniqueId, ProjectId (defaults to office project), GmailThreadId, Subject, FromAddress, ReceivedUtc, Status |
| EmailInboxAttachment | `SiNetSQL\Models\EmailInboxAttachment.cs` (121 lines) | Id, MessageId, OriginalFileName, ContentSha256, AccItemId, ProjectFileId (tag target) |

### Other email-related files

| Component | File | Purpose |
|---|---|---|
| EmailComposerWindow | `SiNetProjectManagerV2\WPF Window\EmailComposerWindow.xaml.cs` | Email composition UI |
| QuoteClassificationDialog | `SiNetProjectManagerV2\Dialogs\QuoteClassificationDialog.xaml.cs` | Quote request classification |
| AssignActionDialog | `SiNetProjectManagerV2\Dialogs\AssignActionDialog.xaml.cs` | Delegate action to employee |
| ActionPermissionWindow | `SiNetProjectManagerV2\Dialogs\ActionPermissionWindow.xaml.cs` | Action permission management |
| WebView2PdfRenderer | `SiNetProjectManagerV2\Services\WebView2PdfRenderer.cs` (808 lines) | WYSIWYG PDF capture from WebView2 |

---

## 3. Current behavior

### 3.1. What the EmailManagementView displays

The main email tab has an RTL layout with these areas:
- **Top status bar**: Connection status, pagination, refresh/calendar toggle, help popup.
- **Project filter bar**: Searchable project selector, job type/status/user filters, Gmail search text + date range.
- **Selected project info bar**: Currently selected project details.
- **Email list** (left column): Grouped email list with color coding, context menus.
- **Email viewer** (center column): Header, attachment bar with ACC upload status, WebView2 body rendering.
- **Action bar**: Filing status, file/unfile/move buttons.
- **Context panel**: Business context chips + suggested action ComboBox.
- **Calendar sidebar** (right column, toggleable): Google Calendar in WebView2.

### 3.2. How email list, preview, and context panel are connected

- `EmailManagementView` sets `DataContext = EmailManagementViewModel` (injected by MainWindow).
- `EmailListControl` inherits the parent `DataContext` (EmailManagementViewModel).
- `EmailViewerControl` receives its ViewModel via `ViewModel` dependency property.
- `EmailContextPanel` receives a separate `DataContext = EmailContextViewModel` (injected via DI in code-behind).
- When `SelectedEmail` changes on the main ViewModel → code-behind calls `EmailContextViewModel.SetEmailMessageAsync(messageId)` → triggers analysis.

### 3.3. How emails are loaded and refreshed

- Gmail OAuth login → `LoginCommand`.
- `LoadEmailsCommand` / `RefreshCommand` → queries DB for `EmailInboxMessage` rows, filtered by project/type/status/date.
- Gmail search: `SearchEmailsCommand` → Gmail API search, syncs results to DB.
- Pagination via `EmailPaginationControl`.
- Auto-refresh on project change via `ActiveProjectContext`.

### 3.4. How Gmail sync affects the UI

Gmail sync (via `EmailIngestionService`) downloads messages and attachments, creates DB records, uploads to ACC. The UI reflects the DB state. Email status transitions (Unassigned → Filed → Synced → Moved) are managed by `EmailWorkflowStateEvaluator`.

### 3.5. How an email is classified or typed

Email classification is primarily through **project association** and **workflow family detection**:
- `EmailContextAnalyzer` determines the workflow family from the associated project's `ProjectType` → `ProjectTypeWorkflowDefinition` mappings.
- Workflow family priority: Review > Opinion > Design.
- `QuoteClassificationDialog` provides manual classification for quote request emails.
- There is no standalone email-type enum — classification is derived from context.

### 3.6. What email types exist

Email states (via `EmailInboxStatus`): these are lifecycle states, not business types:
- Unassigned, Pending, Personal, Irrelevant, Filed, Processing, Error.

Business context is determined by the `EmailContextAnalyzer` based on project association, workflow family, and attachment analysis.

### 3.7. How the available actions are built

The action pipeline is a 3-stage process:

**Stage 1 — Analysis** (`EmailContextAnalyzer.AnalyzeAsync`):
1. Load email + attachments + project from DB.
2. Determine if email is associated to a "real" project (not the default office project).
3. Detect workflow family from project types.
4. Load active workflows via `WorkflowQueryService`.
5. Analyze attachments (count, extensions, tagged count, ACC uploads).
6. Calculate confidence score (High ≥3 axes, Medium=2, Low=1).
7. Returns `EmailContextResult`.

**Stage 2 — Action building** (`SuggestedActionsBuilder.BuildAsync`):
1. `EmailWorkflowStateEvaluator.DetermineStateFromContext()` → email filing state.
2. Branch by association state:
   - **Unassigned**: 7 actions (AssociateToExistingProject, CreatePriceQuote, CreateNewReview, etc.)
   - **Associated (shared)**: 5 actions (AddMaterialToProject, RequestCompletion, etc.)
   - **Workflow-family-specific**: 7-8 actions per family (Design/Review/Opinion)
3. Sort by confidence (descending) then category.
4. Returns `List<SuggestedAction>`.

**Stage 3 — UI presentation** (`EmailContextPanel`):
- Context chips show: project, workflow family, confidence, active workflows, attachments.
- Actions shown in a flat ComboBox (not a tree), sorted by confidence.
- User selects an action and clicks "▶ בצע" (Execute).

### 3.8. The 43 action types

Organized by range:

| Range | Category | Examples |
|---|---|---|
| 100-108 | Unassociated | AssociateToExistingProject, CreatePriceQuote, CreateNewReview, RequestAuthorityInvitation, CreateOpinionProject, CollectMaterial, ForwardToDecision, FileOnly |
| 200-204 | Associated (shared) | AddMaterialToProject, RequestCompletion, PrepareResponse, InternalReview, AddNewDiscipline |
| 300-306 | Design family | HandleComments, UploadNewVersion, UpdateDesign, PrepareSubmission, CoordinateWithConsultants, SendUpdatedMaterial, ReceiveSupplementaryMaterial |
| 400-407 | Review family | ReceiveMaterialForReview, OpenReviewRound, PerformReview, WriteComments, SendComments, TrackCorrections, ReceiveCorrectedVersion, ApproveOrClose |
| 500-506 | Opinion family | ReceiveMaterialForOpinion, AnalyzeDocuments, RequestMissingMaterial, PrepareDraftOpinion, UpdateOpinion, SendOpinion, CloseOpinion |
| 600-602 | Generic backend | StartWorkflow, LinkToProject, CreateTask |

### 3.9. Why the actions list can sometimes be empty

The actions list can be empty when:
1. **No email is selected** — context panel shows idle state ("בחר מייל לניתוח הקשר").
2. **Analysis is in progress** — loading spinner shown.
3. **Email has no DB record** (transient/new Gmail message) — limited context with no attachment analysis.
4. **`EmailContextAnalyzer` returns no usable context** — no project, no workflow family, insufficient data.
5. **`SuggestedActionsBuilder` produces zero actions** — possible if the email state doesn't match any rule branch.

Whether this is expected, a gap, or a bug depends on the specific scenario:
- For unselected/loading states: **expected**.
- For associated emails with workflows: **likely a gap** if zero actions are produced.
- For transient emails: **known limitation** — context is degraded.

### 3.10. Whether actions are controlled by permissions

`ActionPermissionWindow` exists in the Dialogs folder, but the `SuggestedActionsBuilder` does **not** check per-action permissions when building the list. Actions are currently filtered only by email state and workflow family context, not by user permissions.

This is a **known gap**: the permission infrastructure exists but is not integrated into the suggestion pipeline.

### 3.11. Whether actions are controlled by classification

Actions are controlled by the `EmailContextResult`, which includes:
- Whether the email is associated to a project.
- The workflow family (Design/Review/Opinion).
- Active workflow states.
- Attachment analysis.

There is no separate "email classification" step that gates actions — the context analysis itself determines which action branch applies.

### 3.12. Whether actions are controlled by selected project/file/task context

Yes — the `EmailContextAnalyzer` loads the email's associated project, its active workflows, and related tasks/decisions via `TaskLink`. This project context directly determines which workflow-family-specific actions appear.

### 3.13. How email-to-project / email-to-file / email-to-task operations are triggered

| Operation | Trigger | Handler |
|---|---|---|
| File email to project | Context menu "שייך לפרויקט" or ActionBar button | `EmailFilingService.FileAsync` |
| Unfile email | Context menu "בטל שיוך" | `EmailFilingService.UnfileAsync` |
| Move to project | ActionBar "📂 Move to Project" | `MoveToProjectProcessActionHandler` (67KB) |
| Create task from email | Action: CreateTask or typed continuation | `CreateTaskProcessActionHandler` → `TaskFactory` |
| Link to project | Action: LinkToProject | `LinkToProjectProcessActionHandler` |
| Start workflow from email | Action: StartWorkflow | `StartWorkflowProcessActionHandler` |
| Create project from email | Action: CreateNewReview/etc. | Opens `CreateProjectUserControl` via continuation |

### 3.14. How attachments are handled

- Attachments are stored as `EmailInboxAttachment` rows with `ContentSha256` for deduplication.
- ACC upload pipeline: `EmailIngestionService` uploads to Autodesk Construction Cloud.
- Tagging: `AttachmentTaggingService` links attachments to `ProjectFile` entries.
- UI shows ACC upload status (☁✓), tag badges, placement badges per attachment.
- Inline tag selector (`FileTreePicker`) and alternative selector are available per attachment.

### 3.15. How email PDF export works

`WebView2PdfRenderer` (808 lines) captures the email body from WebView2 as PDF using Chromium's print-to-PDF API. This is WYSIWYG rendering — what the user sees is what gets captured.

### 3.16. What diagnostics/logs exist for action-tree issues

- `EmailContextViewModel` has `StatusMessage` property that shows current state text.
- `LastResultMessage` shows the result of the last executed action.
- `IActionLifecycleReporter` in `ActionExecutor` provides lifecycle reporting.
- Selection version guard (`_selectionVersion`) prevents stale async operations — suggests past issues with race conditions.
- No dedicated diagnostic logging for "why zero actions were suggested."

---

## 4. Existing mechanisms to reuse

Before adding any new email action mechanism, these should be reused:

| Mechanism | Service | Purpose |
|---|---|---|
| Email context analysis | `EmailContextAnalyzer` | Determines project/workflow/attachment context |
| Action building | `SuggestedActionsBuilder` | Rule engine for action suggestions |
| Action execution | `ActionExecutor` | Dispatches to domain handlers |
| Email filing | `EmailFilingService` | Single source of truth for file/unfile |
| Continuation system | `IActionContinuationUiHost` + typed continuations | Routes actions to UI dialogs |
| Action handlers | `Domain\Actions\Handlers\*` | 11 implemented handlers |
| Action registry | `ActionDefinitionRegistry` + `ActionCodes` | Complete action catalog |

---

## 5. Active legacy behavior

| Mechanism | Status | Notes |
|---|---|---|
| `GmailVisibleAttachmentsDomExtractor` | **Disabled legacy** | Explicitly disabled with `#if false`, marked as candidate for deletion |
| `EmailManagementViewModel` (347KB) | **Active, oversized** | Contains many responsibilities. Candidate for future decomposition but not deleted now. |
| `ActionPermissionWindow` | **Active, not integrated** | Exists but not wired into the action suggestion pipeline |
| Encoding selector (4 hardcoded options) | **Active legacy** | UTF-8, Windows-1255, ISO-8859-8, Windows-1252 |

---

## 6. Known gaps

| # | Gap | Severity |
|---|---|---|
| 1 | No dedicated diagnostic logging for empty action lists | MEDIUM — makes debugging difficult |
| 2 | Action permissions not integrated into suggestion pipeline | MEDIUM — `ActionPermissionWindow` exists but is not used by `SuggestedActionsBuilder` |
| 3 | Transient emails get degraded context | LOW — no attachment analysis for emails without DB records |
| 4 | No "action tree" hierarchy in UI | LOW — actions are shown as a flat ComboBox list, not a tree |
| 5 | `EmailManagementViewModel` is 347KB | HIGH — extremely large file, many responsibilities |
| 6 | Selection version guard suggests past race conditions | LOW — guard is in place, but root cause may recur |
| 7 | No action audit trail beyond `IActionLifecycleReporter` | LOW — action execution is not persisted as an event history |

---

## 7. Out of Scope

- Implementing new email actions.
- Refactoring the EmailManagementViewModel.
- Integrating ActionPermissionWindow into the suggestion pipeline.
- Adding diagnostic logging.
- Changing Gmail sync behavior.

---

## 8. Dropped / cancelled / postponed

| Item | Status |
|---|---|
| Creating a new parallel email action mechanism | **Cancelled** |
| Adding fallback actions without clear approval | **Not approved** |
| Deleting unused actions | **Not approved** — mark as active legacy / unused / candidate for future cleanup |
| Integrating ActionPermissionWindow into action suggestions | **Postponed** |
| Decomposing EmailManagementViewModel | **Postponed** |
| Code changes | **Not approved** |
| DB changes | **Not approved** |

---

## 9. No-code-change confirmation

- **No code was changed.**
- **No DB was changed.**
- **No Google Sheet was changed.**
- **No data was imported.**
- **No reports, tasks, workflows, or TaskLinks were created.**
- **No old mechanisms were deleted or disabled.**
