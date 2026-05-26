# Decisions

- **Updated:** 26.05.2026
- **Status:** Active container for dated decision records (ADR-style).

## Purpose
Dated, append-only decision records for the application. Each decision is a separate dated file.

## Conventions
- File name pattern: `YYYY-MM-DD-<short-topic>.md`.
- Each file includes a decision date, status, context, decision, and consequences.
- Decisions are not edited after publication; they are superseded by later decisions that reference them.

## Existing decision-log style docs (not yet relocated)
- `Docs\MoveToProject-Decisions-2026-05-24.md` — authoritative for `MoveToProject`.
- `SiNetSQL\docs\WorkflowDecisions.md` — append-only workflow decisions.

These remain in place for now; future rounds may copy or link them here without losing history.
