# AI System Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Skeleton — to be expanded as AI features land.
- **Scope:** AI-assisted features inside the application: what may be sent to an AI service, what must not, where results live, and how AI integrates with tasks, files, and reviews.

## Purpose
Set guardrails for using AI inside the application before any feature is wired in.

## Source of truth
- This document, until a dedicated AI feature ships its own Principles file.

## AI boundary inside business workflows (added 26.05.2026)

This section is the authoritative AI boundary for `PlanReview` /
`Inspection` / `Review` and any other business workflow that uses AI.
It complements
[`Domains\PlanReview\PlanReviewPrinciples-2026-05-26.md`](../PlanReview/PlanReviewPrinciples-2026-05-26.md)
§ *PlanReview / Inspection / Review / AI boundaries* and
[`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Workflow/WorkflowPrinciples-2026-05-26.md)
§ *Workflow / Task / Action handler boundaries*.

- **AI is advisory only.** AI may assist with issue detection, suggested
  comments / notes, content summarisation, initial analysis, and
  recommended checks.
- **AI must not**:
  - approve a plan on its own,
  - reject a plan on its own,
  - close a `Task` on its own,
  - advance a `Workflow` stage on its own,
  - file a file on its own,
  - change business metadata on its own,
  - write back to **DB**, **ACC**, or any **Storage Destination**
    without explicit user confirmation or an agreed `Action Handler`.
- **AI output is not a source of truth.** DB is the source of truth for
  the business process; Storage Destination is the source of truth for
  the physical existence of files; UI is not a source of truth.
- **Promotion of AI output to a business action** must go through one of:
  - explicit **user confirmation**, or
  - an **agreed `Action Handler`**, or
  - an **approved `Workflow` / `Task` path**.
- **AI privacy boundary**: do not send secrets, OAuth tokens,
  credentials, or other sensitive data to AI services without an
  explicit decision. The principle in core item #3 below remains in
  force.

## Core principles
1. **No AI fallback without explicit approval.** If an AI call fails, the system must not silently substitute heuristics that change business outcomes.
2. **Only the minimum necessary content** is sent to an AI service. Personally identifying or sensitive client data is excluded by default.
3. **Customer credentials, secrets, and ACC/Gmail tokens are never sent** to an AI service.
4. **AI results are stored explicitly** (with timestamp, model, and prompt identifier where applicable) so they can be reviewed and superseded later.
5. **AI is advisory.** It does not auto-complete tasks, auto-move files, or auto-create projects without explicit user confirmation.
6. AI features must integrate with existing mechanisms (tasks, files, reviews) rather than creating parallel data stores.
7. Privacy: sending content to AI requires an explicit code path; it must not happen implicitly from generic services.
8. **AI does not approve / reject / close / advance / write business state on its own.** Promotion to a business action requires explicit user confirmation, an agreed `Action Handler`, or an approved `Workflow` / `Task` path (see § *AI boundary inside business workflows*).

## What we do not do now
- Do not auto-execute AI suggestions on tasks or files.
- Do not send raw mail bodies, full attachments, or ACC tokens to AI services.
- Do not create AI-only data stores parallel to existing domain tables.
- Do not introduce silent AI fallbacks.
- Do not let AI approve, reject, close, advance a workflow stage, file a file, or change business metadata on its own.
- Do not write AI output back to DB / ACC / Storage Destination without explicit user confirmation or an agreed `Action Handler`.

## Dropped / cancelled / postponed
- Implicit AI calls from generic services — dropped.
- AI-driven auto-filing — postponed (requires explicit decision).
- Full AI feature catalog — postponed.
- `AI` as an autonomous decision maker — **not approved**.
- `AI` auto-approve / auto-reject / auto-complete / auto-advance — **not approved**.
- Storing `AI` output as business truth without explicit user confirmation — **not approved**.
- Sending secrets / tokens / credentials / sensitive data to AI without an explicit decision — **not approved**.

## Relevant terms / search terms
AI, advisory, privacy, prompt, model, AI result storage, AI fallback, AI auto-action.
