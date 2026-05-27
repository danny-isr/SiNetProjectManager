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
| `SiOffice.GoogleConnector` / `GoogleService` | Email / Google Drive / Google Sheets | Connector | Gmail / Drive / Sheets API access (messages, attachments, threads, drive items) | Reads/writes Google APIs; does not own DB business identity | `EmailIngestionService`, UI email surfaces (`EmailManagementView`) | Business identity decisions (`MessageUniqueId`, `ThreadKey`); DB workflow decisions; persistence of mailbox-local IDs as business data | `SiOffice.GoogleConnector\GoogleService.cs` (`EmailInfo`, `MapMessageToInfo`, attachments helpers) | Active. Google Drive **upload** path is postponed (see `ProjectFilesPrinciples`). |
| `EmailIngestionService` | Email | Application service | Email ingestion, attachment handling, ACC Inbox ingestion flow, `MessageKey` / `MessageUniqueId` derivation via centralized helpers | Reads Gmail (via connector) + RFC822 headers (authoritative for email identity); writes DB email rows; coordinates ACC Inbox upload | Workflow entry points; email management UI; scheduled ingestion | Project filing decisions outside the approved workflow; deriving identity from mailbox-local Gmail IDs | `SiNetSQL\Services\EmailIngestion\EmailIngestionService.cs`, `MessageKeyGenerator.cs`, `SiNetSQL\Models\EmailInboxMessage.cs` | Active. See `EmailSystemPrinciples`, gaps 1–4, 6. |
| `AccInboxReconciliationService` | ACC / Email | Application / Domain service | Verify physical existence in ACC; surface `MissingInAcc` / `StaleAccReference`; layout-aware lookup using `AccInboxLayout` | Reads ACC (authoritative for physical existence when ACC is the configured Storage Destination); reads DB cache; does not treat DB as proof of existence | `MoveToProjectProcessActionHandler`, ACC open / show flows, UI inspection of ACC files | DB-only proof of file existence; deriving viewer URLs from DB IDs; reading mailbox-local Gmail IDs as identity | `AccInboxReconciliationService`, `AccInboxLayout`, `ShowAttachmentInAccAsync` | Active. See `AccSystemPrinciples` and gaps 3, 5, 6. |
| `ProjectFileFilingService` | ProjectFiles | Application / Domain service | File filing pipeline; routing to `ProjectFile` / `ProjectAlternative` / `Storage Destination`; normalization + duplicate prevention for alternative names | Reads/writes DB (`ProjectFile`, `ProjectAlternative`, links); reads Storage Destination state | `IProcessActionHandler` handlers (e.g. `MoveToProjectProcessActionHandler`), external/uploaded file ingestion | UI-only decisions; direct connector bypass; auto-deletion of `ProjectAlternative`; auto-change of Storage Destination based on found copies | `SiNetSQL\Services\ProjectFileFilingService.cs` (and related filing helpers) | Active. See `ProjectFilesPrinciples` and gaps 9, 10. |
| `MoveToProject*` action handler | ProjectFiles / Workflow | Action handler (`IProcessActionHandler`) | Execute the approved `MoveToProject` action: ACC ensure at move time, only required folders created, outcome enrichment | Reads DB (project / file definitions); writes ACC via connector / service; writes DB business links and outcome enrichment | Workflow dispatcher / `IProcessActionHandler` pipeline | Parallel ad-hoc move pipelines; schema or model changes; bypassing the filing service | `MoveToProjectProcessActionHandler`, `MoveToProject-Decisions-2026-05-24.md` | Active. Outcome enrichment must remain backward compatible (see `ProjectFilesPrinciples` §3). |
| `ProjectWorkService`* | ProjectFiles / UI | Application service | Build the `ProjectWork` context and the `ProjectFileInstance` **runtime projection** for the selected project; initial full scan on project entry; later updates via events + focused refresh | Reads DB definitions (`Project`, `ProjectFolder`, `ProjectFile`, `ProjectAlternative`, `Storage Destination`); reads Storage Destination state (authoritative for physical existence); does **not** persist the projection as a source of truth | `ProjectWorkView` ("בעבודה 2"), other UI surfaces consuming the projection | Persisting runtime projection state as permanent business data; broad / system-wide full scans; recurring automatic full rescan on an open project | Project-entry / scan code paths; consumers of `ProjectFileInstance` | Active concept. **Concept name** — verify actual service name in code; record alias under Gaps if it differs. See `ProjectFilesPrinciples` § *ProjectFileInstance — runtime projection*. |
| Workflow services / `WorkflowEngine` | Workflow | Domain / Application service | Workflow stages, transitions, stage gating; task creation / advance via workflow definitions | Reads/writes DB workflow state, `WorkflowStageDefinition`, stage results | UI workflow surfaces; action handlers; task services | Task-only status shortcuts that bypass workflow stages; direct UI state changes | `SiNetSQL` workflow services, `WorkflowStageDefinition` consumers | Active. See `WorkflowPrinciples`. |
| `IProcessActionHandler` dispatcher + handlers | Workflow / ProjectFiles / Email | Action handler layer | Execute approved workflow / task / file actions through a single dispatcher; one handler per action | Reads/writes per handler responsibility; does not own cross-domain truth | Workflow engine, task services, UI commands (via services) | Parallel ad-hoc handler chains; bypassing the dispatcher | `IProcessActionHandler` and concrete handlers (e.g. `MoveToProjectProcessActionHandler`) | Active. New file actions must be implemented as handlers (see `ProjectFilesPrinciples`). |
| Task services | Workflow / Tasks | Application service | Task creation, assignment (incl. UserGroup default-assignee rules), completion, priority (append at end of queue on open/reopen, re-rank on close) | Reads/writes DB task rows and assignments | Workflow engine, UI task surfaces, action handlers | Replace the workflow lifecycle; assign tasks to empty groups silently (must notify) | `SiNetSQL` task services | Active. See `WorkflowPrinciples` and `.github\copilot-instructions.md` §2. |
| Inspection / `PlanReview` services | PlanReview / Diagnostics / Reports | Application service | Inspection and Plan Review business process: lifecycle, statuses, reports | Reads/writes DB plan-review state; reads file material via filing service / ACC | Plan review UI, workflow integrations | File filing or workflow bypass; replacing the main task lifecycle | `Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`, plan-review services | Active. PlanReview is a **separate control lifecycle** (see `ArchitecturePrinciples` § *Business anchors*). |
| Logging / `AppLogger` | Diagnostics | Infrastructure / diagnostics | Structured logging and diagnostics output | None (write-only sink) | All layers | Business state source of truth | `AppLogger` and diagnostics helpers | Active. See `DiagnosticsPrinciples`. |

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
- **`ProjectFileInstance` legacy persistence.** Any remaining
  `UpsertInstanceAsync` / `ProjectFileInstanceId` paths that imply a
  persisted-placement-tracker semantics are legacy; the active principle
  is runtime projection. Tracked in
  `DocumentationVsImplementationGaps-2026-05-26.md` Gap 9 / 10.

## Dropped / cancelled / postponed
- Business logic inside `ViewModel`s as an accepted pattern — **dropped**.
- Creating parallel / duplicate services without checking this catalog —
  **dropped**.
- Bypassing the connector / service boundary from the UI — **dropped**.
- Copilot-generated EF migrations — **dropped** (manual migration rule,
  see `ArchitecturePrinciples`).
- `ProjectFileInstance` as a persisted placement tracker — **superseded**
  by runtime projection (see `ProjectFilesPrinciples`).
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
