# AI System Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Skeleton — to be expanded as AI features land.
- **Scope:** AI-assisted features inside the application: what may be sent to an AI service, what must not, where results live, and how AI integrates with tasks, files, and reviews.

## Purpose
Set guardrails for using AI inside the application before any feature is wired in.

## Source of truth
- This document, until a dedicated AI feature ships its own Principles file.

## Core principles
1. **No AI fallback without explicit approval.** If an AI call fails, the system must not silently substitute heuristics that change business outcomes.
2. **Only the minimum necessary content** is sent to an AI service. Personally identifying or sensitive client data is excluded by default.
3. **Customer credentials, secrets, and ACC/Gmail tokens are never sent** to an AI service.
4. **AI results are stored explicitly** (with timestamp, model, and prompt identifier where applicable) so they can be reviewed and superseded later.
5. **AI is advisory.** It does not auto-complete tasks, auto-move files, or auto-create projects without explicit user confirmation.
6. AI features must integrate with existing mechanisms (tasks, files, reviews) rather than creating parallel data stores.
7. Privacy: sending content to AI requires an explicit code path; it must not happen implicitly from generic services.

## What we do not do now
- Do not auto-execute AI suggestions on tasks or files.
- Do not send raw mail bodies, full attachments, or ACC tokens to AI services.
- Do not create AI-only data stores parallel to existing domain tables.
- Do not introduce silent AI fallbacks.

## Dropped / cancelled / postponed
- Implicit AI calls from generic services — dropped.
- AI-driven auto-filing — postponed (requires explicit decision).
- Full AI feature catalog — postponed.

## Relevant terms / search terms
AI, advisory, privacy, prompt, model, AI result storage, AI fallback, AI auto-action.
