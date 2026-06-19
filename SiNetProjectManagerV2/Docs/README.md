# SiNet Project Manager — Documentation Index

> **Created:** 26.05.2026
> **Updated:** 19.06.2026
> **Status:** Active documentation index — Authorization Verification added.
> **Scope:** Entry point to the `SiNetProjectManagerV2\Docs\` documentation tree.

---

## 1. Purpose

`Docs\` is the central place for living documentation of the SiNet Project
Manager application. It is meant for developers, AI assistants (e.g. GitHub
Copilot) and technical reviewers who need to understand a domain, find the
latest approved decisions, or trace where a mechanism is implemented.

Active documentation lives under **`Domains\`** and **`Decisions\`**.
Historical material lives under **`Archive\`** and is **not** authoritative.

## 2. Document categories

| Category | Location | Purpose |
| --- | --- | --- |
| Domain principles | `Docs\Domains\<Domain>\` | Approved principles per domain. Source of truth. |
| Decisions / ADR | `Docs\Decisions\` | Dated decision records. |
| Archive | `Docs\Archive\` | Historical material — not a source of truth. |
| Feature notes (existing) | top of `Docs\` | A small number of per-feature notes still in place (e.g. `LOGGING.md`, `UI-Consistency-System.md`, `Inspection-Template-Guide.md`). |

## 3. Active domains

| Domain | Principles document |
| --- | --- |
| Architecture | [`Domains\Architecture\ArchitecturePrinciples-2026-05-26.md`](Domains/Architecture/ArchitecturePrinciples-2026-05-26.md) |
| Architecture — Service Catalog | [`Domains\Architecture\ServiceCatalog-2026-05-26.md`](Domains/Architecture/ServiceCatalog-2026-05-26.md) |
| Email / Gmail | [`Domains\Email\EmailSystemPrinciples-2026-05-26.md`](Domains/Email/EmailSystemPrinciples-2026-05-26.md) |
| ACC / Autodesk | [`Domains\ACC\AccSystemPrinciples-2026-05-26.md`](Domains/ACC/AccSystemPrinciples-2026-05-26.md) |
| Database Identity | [`Domains\DatabaseIdentity\DatabaseIdentityPrinciples-2026-05-26.md`](Domains/DatabaseIdentity/DatabaseIdentityPrinciples-2026-05-26.md) |
| Authorization | [`Domains\Authorization\AuthorizationPrinciples-2026-06-18.md`](Domains/Authorization/AuthorizationPrinciples-2026-06-18.md) |
| Authorization — Verification Matrix | [`Domains\Authorization\AuthorizationVerification-2026-06-19.md`](Domains/Authorization/AuthorizationVerification-2026-06-19.md) |
| Project Files | [`Domains\ProjectFiles\ProjectFilesPrinciples-2026-05-26.md`](Domains/ProjectFiles/ProjectFilesPrinciples-2026-05-26.md) |
| Workflow | [`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](Domains/Workflow/WorkflowPrinciples-2026-05-26.md) |
| UI | [`Domains\UI\UiPrinciples-2026-05-26.md`](Domains/UI/UiPrinciples-2026-05-26.md) |
| Deployment | [`Domains\Deployment\DeploymentPrinciples-2026-05-26.md`](Domains/Deployment/DeploymentPrinciples-2026-05-26.md) |
| Diagnostics | [`Domains\Diagnostics\DiagnosticsPrinciples-2026-05-26.md`](Domains/Diagnostics/DiagnosticsPrinciples-2026-05-26.md) |
| AI | [`Domains\AI\AiSystemPrinciples-2026-05-26.md`](Domains/AI/AiSystemPrinciples-2026-05-26.md) |
| Plan Review | [`Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](Domains/PlanReview/PlanReviewPrinciples-2026-05-26.md) |
| Configuration — System Settings Catalog | [`Domains\Configuration\SystemSettingsCatalog-2026-06-18.md`](Domains/Configuration/SystemSettingsCatalog-2026-06-18.md) |
| Configuration — System Health Google Diagnostics Integration | [`Domains\Configuration\SystemHealthGoogleDiagnosticsIntegration-2026-06-19.md`](Domains/Configuration/SystemHealthGoogleDiagnosticsIntegration-2026-06-19.md) |
| Project Work | [`Domains\ProjectWork\ProjectWorkWindow2-2026-06-19.md`](Domains/ProjectWork/ProjectWorkWindow2-2026-06-19.md) |

## 4. Decisions

See [`Decisions\README.md`](Decisions/README.md). Active decision documents:

- [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](Decisions/DocumentationVsImplementationGaps-2026-05-26.md)
  — gap register between approved Principles and current implementation.

Existing decision-style docs still in place (not yet relocated):

- [`MoveToProject-Decisions-2026-05-24.md`](MoveToProject-Decisions-2026-05-24.md)
- `SiNetSQL\docs\WorkflowDecisions.md` (in the SiNetSQL repository)

## 5. Archive

See [`Archive\README.md`](Archive/README.md). Archive is historical only.

**Conflict rule:** if an Archive document contradicts a `Domains` or
`Decisions` document, the `Domains` / `Decisions` document wins.

## 6. Authoring rules for new documents

- Every document includes a date (`DD.MM.YYYY`) and a `Status`.
- Principles documents stay **short**; deep implementation detail goes into
  sub-documents only when needed.
- Each Principles document includes a **Dropped / cancelled / postponed**
  section so disabled mechanisms are not silently revived.
- A new decision that supersedes an older document **adds a pointer**; the
  older document is moved to `Archive\` rather than being deleted.

## 6a. Documentation alignment rule (added 26.05.2026)

**Every meaningful change in the system must include a documentation
check.** Meaningful changes include changes to source of truth,
identifiers, DB / schema / model, Storage Destination, a Service or
Service boundary, Workflow / Task / Action, UI that alters business
behavior, cancelling / postponing / replacing a mechanism, and adding or
removing a fallback.

- If active documentation requires an update, update the relevant
  `Domains\<Domain>\...Principles-...md`, `Decisions\...md`, or the gap
  register
  [`Decisions\DocumentationVsImplementationGaps-2026-05-26.md`](Decisions/DocumentationVsImplementationGaps-2026-05-26.md).
- If no update is required, the change report must explicitly state:
  **`Documentation checked — no update required`**.

See `ArchitecturePrinciples` § *Documentation alignment rule* for the
authoritative wording.

## 7. Things we deliberately do NOT do right now

- Do **not** delete documents.
- Do **not** edit code, DB, schema, migrations, or `ModelSnapshot` as part of documentation work.
- Do **not** rewrite older feature notes; leave them in place or archive them.
- Do **not** silently revive mechanisms listed as Dropped in any Principles document.

## 8. Dropped / cancelled / postponed

- `Docs\work` as an active source of truth — dropped (contents moved to `Archive\work\`).
- Older fix / phase / test notes as active docs — dropped (moved to `Archive\`).
- The 13 old architecture / workflow / import / style audit docs as active — dropped in Round C-Apply (moved to `Archive\`, principles extracted into the new `Architecture`, `Workflow`, `UI`, and `ProjectFiles` Principles).
- Treating old long documents as authoritative — dropped. No active NeedsReview docs remain at the root of `Docs\`.
- `Completed` as a `ProjectStatus` value — dropped (see `WorkflowPrinciples`).
- `Google Drive upload` — postponed (see `ProjectFilesPrinciples`).
- Persisting Gmail mailbox-local `message.id` / `threadId` as DB business data — dropped (see `EmailSystemPrinciples` §2.5 and `DatabaseIdentityPrinciples`).
- ACC Inbox folder names derived from Gmail mailbox-local IDs — dropped; target layout is `THREAD_<ThreadKey>\MSG_<MessageKey>\…` (see `EmailSystemPrinciples` §6.3 and `AccSystemPrinciples`).
- Gmail as a write / management Storage Destination — dropped (see `ProjectFilesPrinciples`).
- Metadata without a defined source-of-truth owner — dropped (see Email / ACC / DatabaseIdentity / ProjectFiles principles).
- Legacy continuation `RequiresUI(...)` enum fallback — not active / candidate for removal (see `WorkflowPrinciples`).
- **Business logic inside `ViewModel`s as an accepted pattern — dropped** (see `ArchitecturePrinciples` and `ServiceCatalog-2026-05-26.md`).
- **Creating parallel / duplicate services without checking the Service Catalog — dropped**.
- **Bypassing the connector / service boundary from the UI — dropped**.
- **Copilot-generated EF migrations — dropped** (manual migration rule, see `ArchitecturePrinciples`).
- **`ProjectFileInstance` as a persisted placement tracker — superseded** by the runtime-projection principle (see `ProjectFilesPrinciples`).
- Full implementation detail inside Principles documents — postponed.
- A fully-complete / exhaustive Service Catalog in one round — postponed (see `ServiceCatalog-2026-05-26.md`).

## 9. Search terms

`Docs index`, `documentation structure`, `SiNet Documentation`, `Domains`,
`Decisions`, `Archive`, `EmailSystemPrinciples`, `AccSystemPrinciples`,
`WorkflowPrinciples`, `MoveToProject-Decisions`, `ADR`, `source of truth`,
`Approved principles`, `Configuration`, `SystemSettingsCatalog`,
`SystemHealthGoogleDiagnosticsIntegration`, `SystemHealthWindow`,
`GoogleDriveFolderDiagnosticService`, `IServiceHealthCheck`, `HealthRow`.