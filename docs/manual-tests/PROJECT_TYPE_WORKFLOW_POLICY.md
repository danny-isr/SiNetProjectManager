# ProjectType ↔ Workflow — policy, integrity, post-approval start

> **Status:** Implemented (2026-07-31)  
> **Related:** [`PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./PROPOSAL_WORKFLOW_MANUAL_TEST.md) §2.8, table `ProjectTypeWorkflowDefinition`

## Rules

1. **Every active JobType (project type) must have ≥1 enabled mapping** to an active `WorkflowDefinition` (`ProjectTypeWorkflowDefinition`). This is an integrity check (System Status / Seed baseline verify). A type without a workflow mapping is invalid configuration.
2. **Admin (standalone):** menu **מדיניות סוג↔תהליך** opens a focused New System window to view/edit mappings (not the full legacy `WorkflowManagementWindow`).
3. **After `QuoteApprovedByClient`:** for the quote project’s project types, resolve each type’s default enabled mapping (`IsDefault` then `SortOrder`). Start **one active project-bound instance per unique `WorkflowDefinitionId`** (dedupe). If any type on the project lacks a mapping → **block completion** with a clear Hebrew error (no open-policy silence).
4. Reject / cancel-no-response paths do **not** start continuation workflows.

## Manual checks

- [ ] System Status / Seed baseline verify reports JobTypes missing workflow mappings (if any).
- [ ] Admin window lists JobTypes + mapped workflows; can set default / enable / change mapping.
- [ ] Fresh Proposal → `QuoteApprovedByClient` → `PRP.Approved` **and** continuation instance(s) started (e.g. Planning).
- [ ] Two project types mapping to the same definition → **one** instance of that definition.
- [ ] Project type with no mapping → approve blocked.

## Soak note

Project 3142 already reached `PRP.Approved` before this wiring; use a **new** Proposal soak to verify auto-start.

### Soak checklist (new Proposal project)

1. Ensure project types on the quote project have enabled mappings (מנהלה → **מדיניות סוג↔תהליך**, or Seed בסיסי).
2. Complete FollowQuote with `QuoteApprovedByClient`.
3. Expect: `PRP.Approved` + project status `WaitingForWorkOrder` **and** one active project-bound instance per unique mapped definition (e.g. PlanningWorkflow).
4. Negative: temporarily disable mapping for a type on the project → approve should fail with Hebrew mapping error; restore mapping before production use.
