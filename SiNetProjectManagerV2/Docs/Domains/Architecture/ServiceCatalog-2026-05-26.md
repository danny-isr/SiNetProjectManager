# Service Catalog

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active service boundary map — **initial catalog**, not a 100% complete inventory. Append-only; extend rather than fork.
- **Scope:** High-level map of the principal services in the system and their responsibility boundaries. Companion to
  [`ArchitecturePrinciples-2026-05-26.md`](ArchitecturePrinciples-2026-05-26.md).

## Purpose
Give developers and AI assistants a single place to look up **which service
owns which responsibility** before adding new code, so that:

- new business logic does not get dropped into `ViewModel`s,
- parallel / duplicate services are not created by accident,
- service boundaries (UI ↔ application service ↔ connector ↔ DB / ACC /
  Gmail) stay stable.

## How to use this catalog

1. Before adding a service, handler, or storage path, **read this catalog**.
2. If an existing service already owns the responsibility — **reuse it**.
3. If an existing service nearly fits — **extend it**.
4. If nothing fits — only then propose a new service, and **add an entry
   here in the same change**.
5. If a name in this catalog is a *concept name* and the real code uses a
   different name, **keep the real name** and note the alias in the
   `Status / notes` column.
6. If a responsibility is split across multiple services, or duplicated —
   **do not refactor in this round**; record it under
   [§ Gaps / overlaps](#gaps--overlaps) and (where relevant) in
   [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](../../Decisions/DocumentationVsImplementationGaps-2026-05-26.md).

## Service boundary rules

- **Meaningful business logic lives in services**, not in `ViewModel`s
  (see `ArchitecturePrinciples` § *Service / Use Case boundaries*).
- **Connectors** (`SiOffice.GoogleConnector`, `SiOffice.AutodeskConnector`,
  the privileged `SiOffice.AccService`) own outbound API calls. They do
  not own business workflow.
- **Application / Domain services** own workflow, filing, identity,
  reconciliation, and Source-of-Truth decisions.
- **DB** is authoritative for project structure (`ProjectFile` →
  `ProjectAlternative`) and business workflow. **`ProjectFileInstance`
  has been removed as an entity** (Stage 9E.4 — Gap 9); no DB row
  represents per-instance file placement, and no DB row is used to prove
  physical file existence (see `ProjectFilesPrinciples`).
- **No silent fallbacks** across service boundaries; missing data / failed
  calls surface visibly.
- **No parallel mechanisms**: see the rules in `ArchitecturePrinciples`.
- **`ProjectFileInstance` is removed.** Any remaining doc text that
  describes it as a persisted placement tracker or as a runtime
  projection entity is superseded. Runtime location resolution is done
  by `IProjectFileLocationResolver` (session cache only); actual
  storage state (ACC / File Server / Google Drive) is the source of
  truth.

## Service table

> **Names with a `*` are *concept names* used for documentation; the real
> code may use a slightly different name. Verify against the codebase and
> note the actual name under `Status / notes` when extending this catalog.**

| Service | Domain | Layer | Responsibility | Source of Truth (read / write) | Called by | Does **not** do | Relevant code areas | Status / notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SiOffice.AccService` | ACC / Deployment | Privileged Windows Service | Privileged ACC operations; central remote ACC orchestration when `AccService:BaseUrl` is configured | Reads/writes ACC via Autodesk APIs; does not own DB business data | Remote WPF clients (`SiNetProjectManagerV2`), CI / deployment tools | UI business logic; Gmail operations; DB workflow decisions | `SiOffice.AccService` (repo: `AutodeskIntegration\SiOffice.AutodeskConnector` / dedicated service project) | Active. Service-mode boundary defined in `ArchitecturePrinciples` §3. |
| `SiOffice.AutodeskConnector` | ACC | Connector | Outbound Autodesk / ACC API calls (items, folders, custom attributes, version history) | Reads/writes ACC; does not own DB business data | `SiOffice.AccService`, ACC-aware services (`AccInboxReconciliationService`, `MoveToProject*`) | UI logic; workflow decisions; DB source-of-truth decisions | `SiOffice.AutodeskConnector` (e.g. `Bim360Service`, `SetItemCustomAttributesAsync`) | Active. Connector only — no workflow rules here. |
| `SiOffice.GoogleConnector` / `GoogleService` | Email / Google Drive / Google Sheets | Connector / service layer | Gmail / Drive / Sheets API access (messages, attachments, threads, drive items, sheets); OAuth / token handling per existing structure | Reads Google APIs; **does not** own DB business identity; **does not** decide Storage Destination | `EmailIngestionService`, UI email surfaces (`EmailManagementView`), `GoogleDriveStore`, domain services that need Google API access | Business identity decisions (`MessageUniqueId`, `ThreadKey`); workflow / filing decisions; PlanReview / AI decisions; Storage Destination decisions; persistence of mailbox-local Gmail IDs as business data; using Sheets as a general business source of truth | `SiOffice.GoogleConnector\GoogleService.cs` (`EmailInfo`, `MapMessageToInfo`, attachments helpers); Google Drive / Sheets helpers in the same connector | Active. Gmail is **read-only ingestion + RFC822 header source**. Google Drive is an **active Storage Destination** reached through `GoogleDriveStore`; **delete** wiring in refile cleanup is postponed. Google Sheets is **integration / reporting / template surface only**. See `EmailSystemPrinciples`, `ProjectFilesPrinciples`, and Gap Register. |
| `EmailIngestionService` | Email | Application service | Email ingestion, attachment handling, ACC Inbox ingestion flow, `MessageKey` / `MessageUniqueId` derivation via centralized helpers | Reads Gmail (via connector) + RFC822 headers (authoritative for email identity); writes DB email rows; coordinates ACC Inbox upload | Workflow entry points; email management UI; scheduled ingestion | Project filing decisions outside the approved workflow; deriving identity from mailbox-local Gmail IDs | `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`, `MessageKeyGenerator.cs`, `SiNetSQL\Models\EmailInboxMessage.cs` | Active. See `EmailSystemPrinciples`, gaps 1–4, 6. |
| `AccInboxReconciliationService` | ACC / Email | Application / Domain service | Verify physical existence in ACC; surface `MissingInAcc` / `StaleAccReference`; layout-aware lookup using `AccInboxLayout` | Reads ACC (authoritative for physical existence when ACC is the configured Storage Destination); reads DB cache; does not treat DB as proof of existence | `MoveToProjectProcessActionHandler`, ACC open / show flows, UI inspection of ACC files | DB-only proof of file existence; deriving viewer URLs from DB IDs; reading mailbox-local Gmail IDs as identity | `AccInboxReconciliationService`, `AccInboxLayout`, `ShowAttachmentInAccAsync` | Active. See `AccSystemPrinciples` and gaps 3, 5, 6. |
| `ProjectFileFilingService` | ProjectFiles | Application / Domain service | File filing pipeline; routing to `ProjectFile` / `ProjectAlternative` / `Storage Destination`; normalization + duplicate prevention for alternative names | Reads/writes DB (`ProjectFile`, `ProjectAlternative`, links); reads Storage Destination state | `IProcessActionHandler` handlers (e.g. `MoveToProjectProcessActionHandler`), external/uploaded file ingestion | UI-only decisions; direct connector bypass; auto-deletion of `ProjectAlternative`; auto-change of Storage Destination based on found copies | `SiNetSQL\Services\ProjectFileFilingService.cs` (and related filing helpers) | Active. See `ProjectFilesPrinciples` and gaps 9, 10. |
| `MoveToProject*` action handler | ProjectFiles / Workflow | Action handler (`IProcessActionHandler`) | Execute the approved `MoveToProject` action: ACC ensure at move time, only required folders created, outcome enrichment | Reads DB (project / file definitions); writes ACC via connector / service; writes DB business links and outcome enrichment | Workflow dispatcher / `IProcessActionHandler` pipeline | Parallel ad-hoc move pipelines; schema or model changes; bypassing the filing service | `MoveToProjectProcessActionHandler`, `MoveToProject-Decisions-2026-05-24.md` | Active. Outcome enrichment must remain backward compatible (see `ProjectFilesPrinciples` §3). |
| `ProjectWorkService`* | ProjectFiles / UI | Application service | Build the `ProjectWork` context for the selected project from DB definitions (`Project`, `ProjectFolder`, `ProjectFile`, `ProjectAlternative`, `Storage Destination`) plus actual storage state resolved at runtime through `IProjectFileLocationResolver`; initial full scan on project entry; later updates via events + focused refresh | Reads DB definitions; reads actual storage state via `IProjectFileLocationResolver` / `IFileStore`; does **not** persist any per-file-instance projection as a source of truth (no `ProjectFileInstance` exists) | `ProjectWorkView` ("בעבודה 2"), other UI surfaces that need the per-project file view | Persisting runtime resolution state as permanent business data; broad / system-wide full scans; recurring automatic full rescan on an open project | Project-entry / scan code paths; consumers of `IProjectFileLocationResolver` | Active concept. **Concept name** — verify actual service name in code; record alias under Gaps if it differs. See `ProjectFilesPrinciples` § *Runtime resolver*. |
| `IProjectFileLocationResolver` / `ProjectFileLocationResolver` | ProjectFiles | Runtime resolver (in-memory session cache) | Resolve, at runtime, where a project file currently lives across the configured Storage Destinations (ACC / File Server / Google Drive) using the `IFileStore` implementations; cache the result for the current session only | Reads actual storage state via `IFileStore`; **does not** own truth and **does not** persist anything; the cache is in-memory and session-scoped | `ProjectWorkService`, filing / refile / open / move flows that previously relied on `ProjectFileInstanceId` | Persist its cache to the DB; replace the removed `ProjectFileInstance` with a new persisted table; perform silent fallback between destinations; act as a source of truth | `SiNetSQL\Services\Files\IProjectFileLocationResolver.cs`, `ProjectFileLocationResolver.cs` | Active. Introduced as part of Stage 9E.1 → 9E.4 to replace `ProjectFileInstanceId` as a placement / filer-state signal. **Session cache only**; not a persisted replacement. |
| `IFileStore` / `FileServerStore` / `AccFileStore` / `GoogleDriveStore` | ProjectFiles / Storage | Storage adapters (uniform store API) | Uniform read / list / upload / open / sidecar surface over actual storage state per Storage Destination: File Server path + sidecar; ACC item / folder / version / metadata; Google Drive file / folder + sidecar | Reads / writes the **actual** storage backend; the backend (and only the backend) is the source of truth for physical existence | `IProjectFileLocationResolver`, `ProjectFileFilingService`, `ProjectFileUploadService`, `IFileOpenService`, `FileIndexService` | Decide routing (that is `ProjectFile.StorageDestination`); silently fall back to another store; auto-pick on duplicate filename | `SiNetSQL\FileIndex\Stores\FileServerStore.cs`, `AccFileStore.cs`, `GoogleDriveStore.cs` | Active. **Google Drive duplicate filename is a conflict** (`FileStoreConflictException`), never an auto-pick. |
| `FileIndexService` | ProjectFiles / Storage | Helper service | Sidecar / `*.si.json` helpers and per-file index utilities used by the stores and by callers that need metadata about a file in its Storage Destination | Reads / writes sidecar metadata next to the file in its backend; does not own placement truth | `IFileStore` implementations, filing / open / refile paths | Act as a source of truth for placement; replace the removed `ProjectFileInstance` | `SiNetSQL\FileIndex\FileIndexService.cs` and related sidecar helpers | Active. Helper layer over the stores; not authoritative on its own. |
| `ICompanyService` / `CompanyService` | Contacts / CRM | Application service | All `Company` / `Contact` persistence: load with contacts, save (update existing + insert new), add, remove | Reads / writes DB (`Company`, `Contact`) via `IDbContextFactory`; does not own cross-domain truth | `CompanyViewModel` (UI state only) | UI display state; filtering / selection (those stay in the ViewModel); schema / migration changes | `SiNetSQL\Services\Companies\ICompanyService.cs`, `CompanyService.cs` | Active. **Pilot** for the ViewModel → Service extraction (gap register Gap 11). Establishes the canonical pattern: ViewModel holds UI state, the service owns DB access. |
| `IUserService` / `UserService` | Users / Identity | Application service | User operations: check duplicate LoginName, add user with admin authorization check, load existing logins, load users with open task counts, update users and trigger ACC membership reconciliation | Reads / writes DB (`Siuser`) via `IDbContextFactory` | `AddUserViewModel`, `UserManagementViewModel` | UI display state; filtering / selection (those stay in the ViewModel); Active Directory user fetching (handled via delegate / connector) | `SiNetSQL\Services\Users\IUserService.cs`, `UserService.cs` | Active. Part of Gap 11 refactoring to extract DB access from User MVVM components. |
| `IContactService` / `ContactService` | Contacts / CRM | Application service | Contact query operations: retrieve all contacts from database | Reads DB (`Contact`) via `IDbContextFactory` | `ContactViewModel` | UI display state; filtering / selection (those stay in the ViewModel) | `SiNetSQL\Services\Contacts\IContactService.cs`, `ContactService.cs` | Active. Part of Gap 11 refactoring to extract DB access from Contact MVVM components. |
| `IPlaceService` / `PlaceService` | Places | Application service | Place query and edit operations: load all places, save place changes | Reads / writes DB (`Place`) via `IDbContextFactory` | `PlaceViewModel` | UI display state; filtering / selection (those stay in the ViewModel) | `SiNetSQL\Services\Places\IPlaceService.cs`, `PlaceService.cs` | Active. Part of Gap 11 refactoring to extract DB access from Place MVVM components. |
| `IProjectTypeService` / `ProjectTypeService` | ProjectTypes | Application service | Project type relation and bid query and update operations: load reference data, add project type relation, remove relation, create or update bid | Reads / writes DB (`JobType`, `TypeOfProjectInProject`, `Bid`) via `IDbContextFactory` | `ProjectTypeViewModel` | UI display state; filtering / selection (those stay in the ViewModel) | `SiNetSQL\Services\ProjectTypes\IProjectTypeService.cs`, `ProjectTypeService.cs` | Active. Part of Gap 11 refactoring to extract DB access from ProjectType MVVM components. |
| `IProjectService` / `ProjectService` | Projects | Application service | Project initialization and creation operations: calculate next project number, query places/companies/contacts/projects data, transactional project creation with default configurations, folder generation, and email/task linking | Reads / writes DB (`Project`, `TypeOfProjectInProject`, `Place`, `Company`, `Contact`, `EmailInboxMessage`, `ProjectAssignment`) via `IDbContextFactory` | `CreateProjectViewModel` | UI display state; validation message display (those stay in the ViewModel) | `SiNetSQL\Services\Projects\IProjectService.cs`, `ProjectService.cs` | Active. Part of Gap 11 refactoring to extract DB access from CreateProject MVVM components. |
| `IStatusMappingService` / `StatusMappingService` | StatusMappings | Application service | Status mapping query and save operations: load status mapping data, save mapping rows, apply mapping if mapping rules exist | Reads / writes DB (`TaskStatusToProjectStatusMapping`, `ProjectAssignmentStatus`, `ProjectStatus`) via `IDbContextFactory` or direct context | `StatusMappingViewModel`, `TaskService` | UI display state; filtering / selection (those stay in the ViewModel) | `SiNetSQL\Services\IStatusMappingService.cs`, `StatusMappingService.cs` | Active. Part of Gap 11 refactoring to extract DB access from StatusMapping MVVM components. Supports legacy sync path for compatibility. |
| Workflow services / `WorkflowEngine` | Workflow | Domain / Application service | Workflow execution, state transitions, and stage validation | Reads/writes DB workflow tables | Services, task orchestrators, action handlers | UI view presentation | `SiNetSQL\Services\Workflow` | Active. |
| `IProcessActionHandler` dispatcher + handlers | Workflow / ProjectFiles / Email | Action handler layer | Execute approved workflow / task / file actions through a single dispatcher; one handler per action; extend existing handlers rather than adding parallel chains | Reads/writes per handler responsibility; does not own cross-domain truth | Workflow engine, task services, UI commands (via services), completion paths | Parallel ad-hoc handler chains; bypassing the dispatcher; new handler creation when an existing handler can be extended | `IProcessActionHandler` and concrete handlers (e.g. `MoveToProjectProcessActionHandler`, `ReviewTask*`, `FileQuoteMaterial*`, `AddMaterialToProject*`, `TaskCompletion*`, `RuntimeAction`-related handlers) | Active. See `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries*. |
| Task services | Workflow / Tasks | Application service | Task creation, assignment (incl. UserGroup default-assignee rules), completion (records result, invokes handler, updates workflow, surfaces and logs failure), priority (append at end of queue on open/reopen, re-rank on close) | Reads/writes DB task rows and assignments | Workflow engine, UI task surfaces, action handlers | Replace the workflow lifecycle; close a `Task` without going through the agreed completion / handler path; assign tasks to empty groups silently (must notify); mark success when the handler failed | `SiNetSQL` task services | Active. See `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries* and `.github\copilot-instructions.md` §2. |
| Inspection / `PlanReview` services | PlanReview / Diagnostics / Reports | Application service | Plan Review business `Workflow`: lifecycle, statuses, reports; reusable `Inspection` / `Review` work component (stage / `Task` / `Action`) with its dedicated UI; integration with the agreed dispatcher (`IProcessActionHandler`) | Reads/writes DB plan-review state (results, reviewer, timestamps, links); reads file material via filing service / ACC; AI output is advisory only | Plan review UI, workflow integrations, action handlers | File filing or workflow bypass; replacing the main task lifecycle; running as a parallel workflow engine; UI directly changing review / workflow / task state; AI auto-approving / auto-rejecting / auto-completing / auto-advancing / writing back business state without an agreed handler | `Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`, plan-review services, dedicated Inspection / Review UI window | Active. PlanReview is a **separate control lifecycle** and a business `Workflow` (see `ArchitecturePrinciples` § *Business anchors* and `PlanReviewPrinciples` § *PlanReview / Inspection / Review / AI boundaries*). |
| Logging / `AppLogger` | Diagnostics | Infrastructure / diagnostics | Structured logging and diagnostics output with explicit tags, business identifiers, Storage Destination, status/reason/failure category | None (write-only sink) | All layers | Business state source of truth; user-facing status delivery (UI is responsible — see `DiagnosticsPrinciples` and the existing **System Status** menu) | `AppLogger` and diagnostics helpers | Active. See `DiagnosticsPrinciples`. |
| Existing **System Status** menu | Diagnostics / UI | UI surface | Show health of central services (e.g. `SiOffice.AccService`, Google / Gmail, `SiOffice.AutodeskConnector`, DB, File Server, WebView2 Runtime, **Autodesk authorization**, **Google authorization**, AI when applicable, ongoing reconciliation / recovery state) | Reads health/status from the responsible services; does not own business data | UI shell / menu | Owning business state; replacing local UI status for item-level problems; running as a parallel notifications mechanism alongside a new one | Existing System Status menu / window | Active. **Extend this existing mechanism** instead of creating a parallel System Status. See `DiagnosticsPrinciples` § *Existing System Status menu* and `DeploymentPrinciples` § *Logs / diagnostics / System Status*. |
| Deployment scripts (publish channels) | Deployment | Build / packaging | Publish + sign + deploy four channels (WPF MSIX, AccService MSI, SyncEngine EXE, SecretImport EXE) to the office network share | Reads project sources; writes to `\\SI-WIN-2K19\AppFolder\AppNet\` | Build machine (manual run by release owner) | Business logic; runtime authorization; in-app modifications | [`publish-all.ps1`](../../../../publish-all.ps1), [`SiNetProjectManagerV2\publish-desktop.ps1`](../../../publish-desktop.ps1), [`SiOffice.AccService\publish-service.ps1`](../../../../SiOffice.AccService/publish-service.ps1), [`MasterPlan.SyncEngine\publish-console.ps1`](../../../../MasterPlan.SyncEngine/publish-console.ps1), [`SiNet.SecretImport\publish-tool.ps1`](../../../../SiNet.SecretImport/publish-tool.ps1) | Active. **Do not create parallel deployment scripts**; extend the existing ones only via a future approved round. See `DeploymentPrinciples` § *Existing deployment artefacts*. |
| Server install / vault provisioner (`Install-OnServer.ps1` + `SiNet.SecretImport`) | Deployment / Authorization | Server-side install | Single elevated script that imports `SiNet.secrets` into the per-user Windows Credential Manager (DPAPI) vault of the service account (default `SI-ENG\sieng`), installs/upgrades `SiOfficeAccService` to run as that same account, and verifies the result | Writes per-user DPAPI vault on `SI-WIN-2K19`; installs the MSI; reads/writes Windows Service registration | Office administrator (manual elevated run on the server) | Per-window authorization; OAuth prompts; storing secrets in source documents | [`SiOffice.AccService\Install-OnServer.ps1`](../../../../SiOffice.AccService/Install-OnServer.ps1), `SiNet.SecretImport` EXE on `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\` | Active. The **only supported server install path**. See `DeploymentPrinciples` § *Centralized Autodesk / Google authorization*. |
| Centralized Autodesk / Google authorization* | Authorization / UI / ACC / Email | Application service / principle | Single central mechanism (or, at minimum, a single central principle) for Autodesk user authorization (three-legged), Google user authorization (Gmail / Drive / Sheets scopes), service-side / two-legged authorization through `SiOffice.AccService`, and WebView2 session / cookies / `UserDataFolder` policy | Reads/writes the central token / session store (platform secure storage on the client; per-user DPAPI vault on the server); does not own business data | All windows / `ViewModel`s that need Autodesk / Google access | Per-window auth flows; per-window token stores; per-window random `UserDataFolder`; silent fallback from `AccService` to a local privileged path; storing tokens in source / unsecured files | `TokenProvider`, `Bim360Service`, `SiOffice.GoogleConnector` / `GoogleService` OAuth helpers, WebView2 host code, `Install-OnServer.ps1` / per-user DPAPI vault on the server | **Concept name** — verify actual implementation in code under a future approved round; record alias under Gaps if it differs. Active principle. See `DeploymentPrinciples` § *Centralized Autodesk / Google authorization* and § *WebView2 profile / `UserDataFolder` policy*. |
| Secrets / credentials / token storage stack | Authorization / Deployment / Diagnostics | Infrastructure | **Service secrets** stored per Windows user in Windows Credential Manager via `CredentialVaultService` (DPAPI, `CRED_PERSIST_LOCAL_MACHINE`); central key list in `SecretKeys`; portable export/import via `SecretProvisioningService` (`SiNet.secrets`, AES-256-CBC + PBKDF2); end-user provisioning via `SecretSetupWindow`; server-side provisioning via `SiNet.SecretImport` + `Install-OnServer.ps1`. **User OAuth tokens** stored separately: Autodesk refresh token in `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` (`TokenProvider`); Google user tokens in the `FileDataStore` under `GoogleReports:TokenStorePath` (default `%APPDATA%\SiNet\GoogleTokens`). **WebView2 session / profile** in `AppConfiguration.WebView2UserDataBasePath` (default `%LOCALAPPDATA%\SiNetProjectManagerV2\WebView2UserData`), per-Google-account subfolder via `WebView2Helper.CreateUserEnvironmentAsync()`, isolated `acc_viewer` subfolder via `WebView2Helper.CreateAccEnvironmentAsync()`. | Reads/writes the three stores listed in the previous column; does not own business data | `SecretSetupWindow`, `AppConfiguration` (vault → file fallback), `TokenProvider`, `GoogleAuthService`, `WebView2Helper`, server install scripts | Storing secrets in `git` / `appsettings*.json` / `*.ps1`; logging secret values, OAuth tokens, refresh tokens, authorization codes, passwords, or cookies; mixing service secrets with user OAuth tokens in one store; creating parallel vaults / token stores / WebView2 profiles; showing secret values in `System Status` | `CredentialVaultService.cs`, `SecretKeys.cs`, `SecretProvisioningService.cs`, `SecretSetupWindow.xaml.cs`, `AppConfiguration.cs` (`GetGoogleClientSecretsPath`, `GoogleTokenStorePath`, `WebView2UserDataBasePath`), `WebView2Helper.cs`, `SiOffice.AutodeskConnector\TokenProvider.cs`, `SiOffice.GoogleConnector\Reports\GoogleAuthService.cs`, `SiNet.SecretImport\Program.cs`, `SiOffice.AccService\Install-OnServer.ps1` | Active. **Three separate concerns:** service secrets, user OAuth tokens, WebView2 session/profile. See `DeploymentPrinciples` § *Secrets / credentials / token storage*. |
| `IActionPermissionService` / `ActionPermissionService` | Authorization | Application service | Centralized deny-by-default action-level authorization. Methods: check user allowed for action, require permission (throws), get authorized users for action, save permission config (admin-only). Administrators bypass action-level checks. Extends existing `CurrentUserContext` role checks and `ActionPermission` model. | Reads/writes DB (`ActionPermission`, `Siuser`) via `IDbContextFactory` | `AssignActionDialog`, `ActionPermissionWindow`, action handlers that need per-action authorization | Role-based access (handled by `CurrentUserContext`); project-level permissions (deferred); UI visibility decisions | `SiNetSQL\Services\Authorization\IActionPermissionService.cs`, `ActionPermissionService.cs` | Active. Created as part of Authorization Alignment Round 1 (2026-06-18). |
| `TaskFactory` | Workflow / Tasks | Application service | Canonical factory for creating `ProjectAssignment` entities with proper timestamps, auto-priority, and audit events. All new task creation code should go through this factory. | Writes DB task rows | `WorkflowStageTaskProvisioningService`, async `TaskService` methods, `TaskLifecycleService` | Inline `new ProjectAssignment` construction; bypassing audit trail | `SiNetSQL\Services\TaskFactory.cs` | Active. See `TaskingWorkflowCoexistence-2026-06-19.md`. |
| `TaskCompletionCoordinator` | Workflow / Tasks | Application service | Centralized task completion: validates event against TaskType, validates result, records event, closes task if policy satisfied, signals `WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync()` for workflow advancement. | Reads/writes DB task and event rows | Workflow engine (via orchestrator), action handlers, UI completion surfaces | Direct `TaskService.ChangeTaskStatus()` calls that bypass workflow advance; closing tasks without recording results | `SiNetSQL\Services\Tasks\TaskCompletionCoordinator.cs` | Active. See `TaskingWorkflowCoexistence-2026-06-19.md`. |
| `TaskLifecycleService` | Workflow / Tasks | Application service | Evaluates `TaskBehaviorDefinition` + `TaskTriggerRule` + `TaskCompletionRule` for event-driven auto-create and auto-close of tasks. Handles micro-task lifecycle (e.g., auto-create MaterialFiling task when email assigned to project). | Reads/writes DB task and behavior rows | Event sources (email assignment, attachment tagging), `TaskBehaviorSeedService` | Replacing workflow stage tasks; creating parallel auto-create mechanisms | `SiNetSQL\Services\TaskLifecycle\TaskLifecycleService.cs` | Active legacy mechanism. Complementary to workflow-stage tasks. |
| `WorkflowTaskOrchestrator` | Workflow | Domain / Application service | Bridge between `WorkflowEngine` and the task system. Wraps engine start/advance with task provisioning. `CheckAndAutoAdvanceAsync()` evaluates transition triggers after task status changes and auto-fires if evaluation mode is Auto. | Reads/writes DB workflow and task rows | `WorkflowEngine`, `TaskCompletionCoordinator`, `WorkflowStageTaskProvisioningService` | Direct workflow advance from UI/ViewModel; parallel orchestration mechanisms | `SiNetSQL\Services\Workflow\WorkflowTaskOrchestrator.cs` | Active. |
| `WorkflowStageTaskProvisioningService` | Workflow / Tasks | Application service | Creates `ProjectAssignment` tasks from `WorkflowStageTask` templates via `TaskFactory.CreateAsync()`. Handles group-based fallback when no stage templates exist. | Reads DB stage task templates; writes task rows via `TaskFactory` | `WorkflowTaskOrchestrator` | Inline task creation; bypassing `TaskFactory` | `SiNetSQL\Services\Workflow\WorkflowStageTaskProvisioningService.cs` | Active. |
| `WorkflowSeedService` | Workflow | Application service | Idempotent seeding of all workflow definitions (PLN, MAT, REV, PRP, OPN), project-type mappings, stage activations, and disciplines. Called on startup and after dev data reset. | Reads/writes DB workflow definition tables | App startup, `DevDataResetService` | Manual DB inserts; ad-hoc seeding scripts | `SiNetSQL\Services\Workflow\WorkflowSeedService.cs` | Active. |
| `ProjectTypeRuleService` | Tasks / Configuration | Application service | Queries `ProjectTypeTaskType` and `ProjectTypeStatus` junction tables to filter allowed task types and statuses per project type at runtime. | Reads DB junction tables | Task creation UI, admin configuration | Hardcoding allowed types; bypassing admin-configurable filtering | `SiNetSQL\Services\ProjectTypeRuleService.cs` | Active legacy mechanism. See `TaskingWorkflowCoexistence-2026-06-19.md`. |
| `TaskManagementSeedService` | Tasks / Configuration | Application service | Seeds static TaskType and Status lookup data on every app startup (`EnsureStaticLookupData`). `ResetMappingsToDefaults()` wipes and re-seeds `ProjectTypeTaskType` and `ProjectTypeStatus` mappings. Also seeds `TaskResultDefinition` rows. | Reads/writes DB lookup and junction tables | App startup, `DevDataResetService` | Ad-hoc lookup creation; skipping startup seeding | `SiNetSQL\Services\TaskManagementSeedService.cs` | Active. |
| `GoogleSheetReviewMigrationPreviewService` | Migration / Plan Review / Workflow | Application service (read-only) | Builds read-only preview rows for Google Sheet Review Migration. Reads Google Sheets via `IndexSheetReader`, resolves projects (in-memory), reads workflow state via `WorkflowQueryService`, checks existing `InspectionReport` rows, loads JSON cache via `ExtractionCacheService`, classifies preview rows. **Does not** call `SaveChanges`, `Add`, `Update`, `Remove`, `CreateReportAsync`, `StartWorkflowAsync`, `ReassignTask`, or any write operation. | Reads DB (projects, workflows, reports, users) via `IDbContextFactory`; reads Google Sheets via connector; reads local JSON cache | `MigrationPocWindow` Tab 3 / Phase 1 Preview UI | Writing to DB; creating reports/workflows/tasks; committing migration rows; owning workflow execution | `SiNetProjectManagerV2\Services\Migration\GoogleSheetReviewMigrationPreviewService.cs` | Active. Phase 1 read-only preview service. See `GoogleSheetReviewMigrationDesign-2026-06-21.md`. |

## Gaps / overlaps

These are **documentation-only** observations. They do **not** trigger code
changes in this round. Where appropriate, an entry is also linked from
`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`.

- **Concept-vs-real names.** `ProjectWorkService` in this catalog is a
  *concept name*. The real code may expose this responsibility under a
  different service / class (or split across multiple). **Needs code
  verification.** Do not rename anything in this round.
- **Business logic in `ViewModel`s.** Several existing `ViewModel`s may
  still hold non-trivial business decisions (filing routing, identity
  derivation, workflow shortcuts). This is **not fixed here**; record
  concrete cases under
  `DocumentationVsImplementationGaps-2026-05-26.md` when discovered.
- **Service overlap candidates.** ACC operations are reachable from at
  least three places (`SiOffice.AccService`, `SiOffice.AutodeskConnector`,
  `AccInboxReconciliationService`). The **authoritative split** is now
  documented in
  [`Domains\ACC\AccSystemPrinciples-2026-05-26.md`](../ACC/AccSystemPrinciples-2026-05-26.md)
  § *ACC service boundaries*:
  - `SiOffice.AccService` — privileged / service-mode ACC operations.
  - `SiOffice.AutodeskConnector` — technical API connector only (no
    business decisions).
  - `AccInboxReconciliationService` — ACC Inbox existence / status
    verification only (no upload / no filing / no ACC project creation /
    no DB-only fallback).
  - UI does **not** call `SiOffice.AutodeskConnector` directly for
    business decisions. Make sure new ACC code routes through the
    boundary defined in `ArchitecturePrinciples` §3 and `AccSystemPrinciples`
    § *ACC service boundaries* instead of adding a fourth path.
- **`MoveToProject` parallel paths.** Any code that performs ACC ensure /
  move outside `MoveToProjectProcessActionHandler` is a candidate overlap
  — **document, do not refactor in this round**.
- **Workflow / Task / Action handler boundary.** The authoritative split
  is documented in
  [`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Workflow/WorkflowPrinciples-2026-05-26.md)
  § *Workflow / Task / Action handler boundaries*. In short:
  - `Workflow` is the long-running process (stages / transitions).
  - `Task` is a unit of work; it does not replace the workflow.
  - `Action Handler` is where business actions execute, via the agreed
    dispatcher (e.g. `IProcessActionHandler`); extend existing handlers
    instead of creating parallel ad-hoc chains.
  - `RuntimeAction` is an action description / state, **not** a parallel
    workflow engine, and must not bypass `Workflow` / `Task` /
    `Action Handler` / `Completion`.
  - `Completion` records result, invokes handler when needed, updates
    workflow when needed, surfaces and logs failure, and never hides a
    handler failure behind a fallback.
  - UI / ViewModels do **not** close workflows, change stages directly,
  or execute business actions; they call Service / Dispatcher / Handler
    / Use Case and surface the result.
- **Google service boundary (Gmail / Drive / Sheets).** Google access is
  reachable through a single connector / service layer
  (`SiOffice.GoogleConnector` / `GoogleService`). The **authoritative
  split** is documented in
  [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](../Email/EmailSystemPrinciples-2026-05-26.md)
  and
  [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](../ProjectFiles/ProjectFilesPrinciples-2026-05-26.md):
  - **Gmail** — read-only ingestion + RFC822 header source. Mailbox-local
    `message.id` / `threadId` are runtime only, not persisted as business
    data. Gmail is **not** a write Storage Destination.
  - **Google Drive** — an **active Storage Destination**. Drive uploads
    go through `ProjectFileUploadService` → `GoogleDriveStore`.
    Duplicate filename in the target folder is a **conflict** — no
    silent auto-pick and no silent fallback. Drive **delete** wiring in
    refile cleanup is **postponed**.
  - **Google Sheets** — integration / reporting / template surface only;
    not a general business source of truth.
  - `SiOffice.GoogleConnector` / `GoogleService` must **not** host
    business rules of `ProjectFiles` / `Workflow` / `PlanReview` / AI /
    Storage Destination. Domain services decide; the connector provides
    API operations.
- **`ProjectFileInstance` removal.** `ProjectFileInstance` has been
  **removed** as an entity / `DbSet` / table (Stage 9E.4 — Gap 9). Any
  remaining `UpsertInstanceAsync` / `ProjectFileInstanceId` references in
  older documentation are obsolete. Runtime resolution is performed by
  `IProjectFileLocationResolver` (session cache only). Migration
  `RemoveProjectFileInstanceTable` drops the table, the two FKs, the two
  indexes, and the `EmailInboxAttachment.ProjectFileInstanceId` /
  `InspectionReportDrawing.FileInstanceId` columns; `Update-Database` is
  user-run. See `DocumentationVsImplementationGaps-2026-05-26.md` Gap 9.
- **Diagnostics / user-visible status boundary.** Logs are a developer
  channel only. User-impacting failures must reach the UI via the
  **existing System Status** menu (system-level health) or **local UI
  status** (item-level problems). A new parallel System Status mechanism
  is **not approved**. See
  [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](../Diagnostics/DiagnosticsPrinciples-2026-05-26.md).
- **PlanReview / Inspection / Review / AI boundary.** The authoritative
  split is documented in
  [`Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](../PlanReview/PlanReviewPrinciples-2026-05-26.md)
  § *PlanReview / Inspection / Review / AI boundaries* and
  [`Domains\AI\AiSystemPrinciples-2026-05-26.md`](../AI/AiSystemPrinciples-2026-05-26.md)
  § *AI boundary inside business workflows*:
  - `PlanReview` is a business `Workflow` for reviewing plans and uses
    the regular `Workflow` / `Task` / `Action Handler` mechanisms; no
    parallel workflow engine.
  - `Inspection` / `Review` is a reusable work component inside a
    `Workflow` with a dedicated UI window; it may be a stage, `Task`,
    or `Action` depending on context, and it is **not** a stand-alone
    workflow engine and **not** "just UI".
  - The dedicated UI window invokes agreed Services / Handlers and does
    **not** change business state directly.
  - `AI` is advisory only and must not approve, reject, close, advance
    a workflow stage, or write business state on its own; promotion to
    a business action requires explicit user confirmation, an agreed
    `Action Handler`, or an approved `Workflow` / `Task` path.
- **Deployment / centralized authorization boundary.** The authoritative
  rules are documented in
  [`Domains\Deployment\DeploymentPrinciples-2026-05-26.md`](../Deployment/DeploymentPrinciples-2026-05-26.md):
  - Four publish channels exist today (`publish-all.ps1` orchestrating
    `publish-desktop.ps1` / `publish-service.ps1` /
    `publish-console.ps1` / `publish-tool.ps1`); the server install is
    `SiOffice.AccService\Install-OnServer.ps1`. **Do not** create
    parallel deployment scripts.
  - **Autodesk / Google authorization is centralized and reused** across
    windows; no per-window auth flow, no per-window token store.
  - **WebView2** uses an explicit `UserDataFolder` / profile policy;
    windows that must share login share the profile, and any deliberate
    isolation is documented.
  - **`System Status`** is the existing central health surface; it must
    reflect ACC Service / DB / File Server / Google / Gmail / WebView2
    Runtime / Autodesk authorization / Google authorization / AI (if
    relevant). A parallel `System Status` is **not approved**.
  - **At startup**: lightweight availability checks and `System Status`
    updates only — no global scan, no migrations, no ACC project
    creation, no wide provisioning, no automatic uploads, no workflow
    changes, no silent fallback from `AccService` to a local path.

## Dropped / cancelled / postponed
- Business logic inside `ViewModel`s as an accepted pattern — **dropped**.
- Creating parallel / duplicate services without checking this catalog —
  **dropped**.
- Bypassing the connector / service boundary from the UI — **dropped**.
- Copilot-generated EF migrations — **dropped** (manual migration rule,
  see `ArchitecturePrinciples`).
- `ProjectFileInstance` as a persisted placement tracker — **removed**
  (Stage 9E.4 — Gap 9). Entity, `DbSet`, configuration, and
  `ProjectFileUploadService` deleted; migration
  `RemoveProjectFileInstanceTable` drops the table and related FKs /
  columns; `Update-Database` is user-run. Runtime resolution is now done
  by `IProjectFileLocationResolver` (session cache only).
- Gmail as a write Storage Destination — **dropped** (read-only ingestion).
- Persisting Gmail local IDs (`message.id` / `threadId`) in the DB as
  business identifiers — **dropped**.
- A new Google Drive upload mechanism without an explicit decision —
  **completed** (Drive is now an active Storage Destination via
  `GoogleDriveStore`; duplicate filename is a conflict; Drive delete
  wiring in refile remains postponed).
- Google Drive fallback when ACC / File Server is missing — **not approved**.
- `GoogleService` as a general business engine (workflow / filing /
  PlanReview / AI) — **dropped**.
- Google Sheets as a general business source of truth — **not approved**.
- Parallel action flow outside the agreed dispatcher — **not approved**.
- Direct `Task` closure that bypasses the agreed completion / service / handler path — **not approved**.
- Direct workflow stage changes from the UI / `ViewModel` — **not approved**.
- `ProjectStatus` used as a `WorkflowStage` — **not approved**.
- `RuntimeAction` as an additional workflow engine — **not approved**.
- Fallback that signals success despite a failed handler — **not approved**.
- A new System Status / notifications mechanism in parallel to the
  existing one — **not approved**.
- `log`-only error reporting when the failure affects the user — **not
  approved**.
- Vague user messages such as a bare `Metadata error` without a clear
  interpretation — **not approved**.
- Re-enabling disabled diagnostic mechanisms without an approval round
  — **not approved**.
- `Inspection` / `Review` as a stand-alone workflow engine — **not approved**.
- `PlanReview` as a silent side effect of file filing — **not approved**.
- UI that changes `Review` / `Workflow` / `Task` state directly — **not approved**.
- `AI` as an autonomous decision maker — **not approved**.
- `AI` auto-approve / auto-reject / auto-complete / auto-advance — **not approved**.
- Storing `AI` output as business truth without explicit user confirmation — **not approved**.
- Per-window Autodesk / Google authorization flows — **not approved**.
- Per-window token store or per-window WebView2 `UserDataFolder` without a documented isolation reason — **not approved**.
- Silent fallback from `SiOffice.AccService` to a local privileged path when `AccService:BaseUrl` is configured — **not approved**.
- Startup-time global scan / EF migrations / ACC project creation / wide provisioning / automatic uploads / workflow changes — **not approved**.
- Creating a new deployment script in parallel to the existing publish channels — **not approved** (extend the existing ones only via a future approved round).
- Storing service secrets, OAuth tokens, refresh tokens, authorization codes, passwords, or cookies in `git`, `appsettings*.json`, or `*.ps1` scripts — **not approved**.
- Logging secret values, OAuth tokens, refresh tokens, authorization codes, passwords, or cookies — **not approved**.
- Mixing service secrets with user OAuth tokens in a single store — **not approved**.
- Creating a new vault / token store / WebView2 profile in parallel to the existing ones — **not approved**.
- Showing secret values in `System Status` — **not approved** (show configured / missing / expired / unreachable only).
- Moving secrets across stores in this round — **not in this round**.
- A fully-complete / exhaustive Service Catalog in a single round —
  **postponed**; this is an initial catalog and will be extended in
  follow-up documentation rounds.

## Relevant terms / search terms
ServiceCatalog, service boundary, use case, application service, domain
service, connector, ViewModel boundary, SiOffice.AccService,
SiOffice.AutodeskConnector, SiOffice.GoogleConnector, GoogleService,
EmailIngestionService, AccInboxReconciliationService,
ProjectFileFilingService, MoveToProjectProcessActionHandler,
ProjectWorkService, ProjectFileInstance runtime projection,
WorkflowEngine, IProcessActionHandler, Task services, PlanReview,
AppLogger, business anchor, Workflow vs Task, Documentation alignment
rule, Manual migration rule.
