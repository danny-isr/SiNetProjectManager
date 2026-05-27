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
  `ProjectAlternative`) and business workflow; `ProjectFileInstance` is a
  **runtime projection**, not a persisted placement tracker (see
  `ProjectFilesPrinciples`).
- **No silent fallbacks** across service boundaries; missing data / failed
  calls surface visibly.
- **No parallel mechanisms**: see the rules in `ArchitecturePrinciples`.
- **`ProjectFileInstance` is not a persisted placement tracker.** Any
  legacy text describing it as such is superseded.

## Service table

> **Names with a `*` are *concept names* used for documentation; the real
> code may use a slightly different name. Verify against the codebase and
> note the actual name under `Status / notes` when extending this catalog.**

| Service | Domain | Layer | Responsibility | Source of Truth (read / write) | Called by | Does **not** do | Relevant code areas | Status / notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SiOffice.AccService` | ACC / Deployment | Privileged Windows Service | Privileged ACC operations; central remote ACC orchestration when `AccService:BaseUrl` is configured | Reads/writes ACC via Autodesk APIs; does not own DB business data | Remote WPF clients (`SiNetProjectManagerV2`), CI / deployment tools | UI business logic; Gmail operations; DB workflow decisions | `SiOffice.AccService` (repo: `AutodeskIntegration\SiOffice.AutodeskConnector` / dedicated service project) | Active. Service-mode boundary defined in `ArchitecturePrinciples` §3. |
| `SiOffice.AutodeskConnector` | ACC | Connector | Outbound Autodesk / ACC API calls (items, folders, custom attributes, version history) | Reads/writes ACC; does not own DB business data | `SiOffice.AccService`, ACC-aware services (`AccInboxReconciliationService`, `MoveToProject*`) | UI logic; workflow decisions; DB source-of-truth decisions | `SiOffice.AutodeskConnector` (e.g. `Bim360Service`, `SetItemCustomAttributesAsync`) | Active. Connector only — no workflow rules here. |
| `SiOffice.GoogleConnector` / `GoogleService` | Email / Google Drive / Google Sheets | Connector / service layer | Gmail / Drive / Sheets API access (messages, attachments, threads, drive items, sheets); OAuth / token handling per existing structure | Reads Google APIs; **does not** own DB business identity; **does not** decide Storage Destination | `EmailIngestionService`, UI email surfaces (`EmailManagementView`), domain services that need Google API access | Business identity decisions (`MessageUniqueId`, `ThreadKey`); workflow / filing / `ProjectFileInstance` decisions; PlanReview / AI decisions; Storage Destination decisions; persistence of mailbox-local Gmail IDs as business data; using Sheets as a general business source of truth | `SiOffice.GoogleConnector\GoogleService.cs` (`EmailInfo`, `MapMessageToInfo`, attachments helpers); Google Drive / Sheets helpers in the same connector | Active. Gmail is **read-only ingestion + RFC822 header source**. Google Drive is a **possible Storage Destination separate from Gmail**, but **upload is postponed** — no new Drive upload mechanism / fallback without an explicit decision. Google Sheets is **integration / reporting / template surface only**. See `EmailSystemPrinciples`, `ProjectFilesPrinciples`, and Gap Register. |
| `EmailIngestionService` | Email | Application service | Email ingestion, attachment handling, ACC Inbox ingestion flow, `MessageKey` / `MessageUniqueId` derivation via centralized helpers | Reads Gmail (via connector) + RFC822 headers (authoritative for email identity); writes DB email rows; coordinates ACC Inbox upload | Workflow entry points; email management UI; scheduled ingestion | Project filing decisions outside the approved workflow; deriving identity from mailbox-local Gmail IDs | `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`, `MessageKeyGenerator.cs`, `SiNetSQL\Models\EmailInboxMessage.cs` | Active. See `EmailSystemPrinciples`, gaps 1–4, 6. |
| `AccInboxReconciliationService` | ACC / Email | Application / Domain service | Verify physical existence in ACC; surface `MissingInAcc` / `StaleAccReference`; layout-aware lookup using `AccInboxLayout` | Reads ACC (authoritative for physical existence when ACC is the configured Storage Destination); reads DB cache; does not treat DB as proof of existence | `MoveToProjectProcessActionHandler`, ACC open / show flows, UI inspection of ACC files | DB-only proof of file existence; deriving viewer URLs from DB IDs; reading mailbox-local Gmail IDs as identity | `AccInboxReconciliationService`, `AccInboxLayout`, `ShowAttachmentInAccAsync` | Active. See `AccSystemPrinciples` and gaps 3, 5, 6. |
| `ProjectFileFilingService` | ProjectFiles | Application / Domain service | File filing pipeline; routing to `ProjectFile` / `ProjectAlternative` / `Storage Destination`; normalization + duplicate prevention for alternative names | Reads/writes DB (`ProjectFile`, `ProjectAlternative`, links); reads Storage Destination state | `IProcessActionHandler` handlers (e.g. `MoveToProjectProcessActionHandler`), external/uploaded file ingestion | UI-only decisions; direct connector bypass; auto-deletion of `ProjectAlternative`; auto-change of Storage Destination based on found copies | `SiNetSQL\Services\ProjectFileFilingService.cs` (and related filing helpers) | Active. See `ProjectFilesPrinciples` and gaps 9, 10. |
| `MoveToProject*` action handler | ProjectFiles / Workflow | Action handler (`IProcessActionHandler`) | Execute the approved `MoveToProject` action: ACC ensure at move time, only required folders created, outcome enrichment | Reads DB (project / file definitions); writes ACC via connector / service; writes DB business links and outcome enrichment | Workflow dispatcher / `IProcessActionHandler` pipeline | Parallel ad-hoc move pipelines; schema or model changes; bypassing the filing service | `MoveToProjectProcessActionHandler`, `MoveToProject-Decisions-2026-05-24.md` | Active. Outcome enrichment must remain backward compatible (see `ProjectFilesPrinciples` §3). |
| `ProjectWorkService`* | ProjectFiles / UI | Application service | Build the `ProjectWork` context and the `ProjectFileInstance` **runtime projection** for the selected project; initial full scan on project entry; later updates via events + focused refresh | Reads DB definitions (`Project`, `ProjectFolder`, `ProjectFile`, `ProjectAlternative`, `Storage Destination`); reads Storage Destination state (authoritative for physical existence); does **not** persist the projection as a source of truth | `ProjectWorkView` ("בעבודה 2"), other UI surfaces consuming the projection | Persisting runtime projection state as permanent business data; broad / system-wide full scans; recurring automatic full rescan on an open project | Project-entry / scan code paths; consumers of `ProjectFileInstance` | Active concept. **Concept name** — verify actual service name in code; record alias under Gaps if it differs. See `ProjectFilesPrinciples` § *ProjectFileInstance — runtime projection*. |
| Workflow services / `WorkflowEngine` | Workflow | Domain / Application service | Workflow stages, transitions, stage gating; task creation / advance via workflow definitions | Reads/writes DB workflow state, `WorkflowStageDefinition`, stage results | UI workflow surfaces; action handlers; task services | Task-only status shortcuts that bypass workflow stages; direct UI state changes; using `ProjectStatus` as a `WorkflowStage`; running as a `RuntimeAction`-only engine | `SiNetSQL` workflow services, `WorkflowStageDefinition` consumers | Active. See `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries*. |
| `IProcessActionHandler` dispatcher + handlers | Workflow / ProjectFiles / Email | Action handler layer | Execute approved workflow / task / file actions through a single dispatcher; one handler per action; extend existing handlers rather than adding parallel chains | Reads/writes per handler responsibility; does not own cross-domain truth | Workflow engine, task services, UI commands (via services), completion paths | Parallel ad-hoc handler chains; bypassing the dispatcher; new handler creation when an existing handler can be extended | `IProcessActionHandler` and concrete handlers (e.g. `MoveToProjectProcessActionHandler`, `ReviewTask*`, `FileQuoteMaterial*`, `AddMaterialToProject*`, `TaskCompletion*`, `RuntimeAction`-related handlers) | Active. See `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries*. |
| Task services | Workflow / Tasks | Application service | Task creation, assignment (incl. UserGroup default-assignee rules), completion (records result, invokes handler, updates workflow, surfaces and logs failure), priority (append at end of queue on open/reopen, re-rank on close) | Reads/writes DB task rows and assignments | Workflow engine, UI task surfaces, action handlers | Replace the workflow lifecycle; close a `Task` without going through the agreed completion / handler path; assign tasks to empty groups silently (must notify); mark success when the handler failed | `SiNetSQL` task services | Active. See `WorkflowPrinciples` § *Workflow / Task / Action handler boundaries* and `.github\copilot-instructions.md` §2. |
| Inspection / `PlanReview` services | PlanReview / Diagnostics / Reports | Application service | Plan Review business `Workflow`: lifecycle, statuses, reports; reusable `Inspection` / `Review` work component (stage / `Task` / `Action`) with its dedicated UI; integration with the agreed dispatcher (`IProcessActionHandler`) | Reads/writes DB plan-review state (results, reviewer, timestamps, links); reads file material via filing service / ACC; AI output is advisory only | Plan review UI, workflow integrations, action handlers | File filing or workflow bypass; replacing the main task lifecycle; running as a parallel workflow engine; UI directly changing review / workflow / task state; AI auto-approving / auto-rejecting / auto-completing / auto-advancing / writing back business state without an agreed handler | `Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`, plan-review services, dedicated Inspection / Review UI window | Active. PlanReview is a **separate control lifecycle** and a business `Workflow` (see `ArchitecturePrinciples` § *Business anchors* and `PlanReviewPrinciples` § *PlanReview / Inspection / Review / AI boundaries*). |
| Logging / `AppLogger` | Diagnostics | Infrastructure / diagnostics | Structured logging and diagnostics output with explicit tags, business identifiers, Storage Destination, status/reason/failure category | None (write-only sink) | All layers | Business state source of truth; user-facing status delivery (UI is responsible — see `DiagnosticsPrinciples` and the existing **System Status** menu) | `AppLogger` and diagnostics helpers | Active. See `DiagnosticsPrinciples`. |
| Existing **System Status** menu | Diagnostics / UI | UI surface | Show health of central services (e.g. `SiOffice.AccService`, Google / Gmail, `SiOffice.AutodeskConnector`, DB, AI when applicable, ongoing reconciliation / recovery state) | Reads health/status from the responsible services; does not own business data | UI shell / menu | Owning business state; replacing local UI status for item-level problems; running as a parallel notifications mechanism alongside a new one | Existing System Status menu / window | Active. **Extend this existing mechanism** instead of creating a parallel System Status. See `DiagnosticsPrinciples` § *Existing System Status menu*. |

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
  - **Google Drive** — a **possible Storage Destination** separate from
    Gmail. Drive **upload is postponed**; no new Drive upload mechanism
    or fallback may be added without an explicit decision. Drive does
    not replace DB as the business source of truth.
  - **Google Sheets** — integration / reporting / template surface only;
    not a general business source of truth.
  - `SiOffice.GoogleConnector` / `GoogleService` must **not** host
    business rules of `ProjectFiles` / `Workflow` / `PlanReview` / AI /
    Storage Destination. Domain services decide; the connector provides
    API operations.
- **`ProjectFileInstance` legacy persistence.** Any remaining
  `UpsertInstanceAsync` / `ProjectFileInstanceId` paths that imply a
  persisted-placement-tracker semantics are legacy; the active principle
  is runtime projection. Tracked in
  `DocumentationVsImplementationGaps-2026-05-26.md` Gap 9 / 10.
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

## Dropped / cancelled / postponed
- Business logic inside `ViewModel`s as an accepted pattern — **dropped**.
- Creating parallel / duplicate services without checking this catalog —
  **dropped**.
- Bypassing the connector / service boundary from the UI — **dropped**.
- Copilot-generated EF migrations — **dropped** (manual migration rule,
  see `ArchitecturePrinciples`).
- `ProjectFileInstance` as a persisted placement tracker — **superseded**
  by runtime projection (see `ProjectFilesPrinciples`).
- Gmail as a write Storage Destination — **dropped** (read-only ingestion).
- Persisting Gmail local IDs (`message.id` / `threadId`) in the DB as
  business identifiers — **dropped**.
- A new Google Drive upload mechanism without an explicit decision —
  **not approved**.
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
