# Plan Review Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Skeleton — to be expanded as the Plan Review feature lands.
- **Scope:** Plan review as a stand-alone domain: dedicated UI window, business process, reviewed files, results, optional AI assistance, and links back to project / task / file.

## Purpose
Define Plan Review as its own domain rather than as a side effect of filing or tasks.

## Source of truth
- This document for principles. Detailed implementation docs will be added under this folder when the feature lands.

## Core principles
1. **Plan Review is its own business process.** It has a dedicated UI window and is not embedded as a side effect of generic file filing.
2. **Reviewed files** are drawings / plans from ACC (or external) that have been filed into the project. The review references the file by its authoritative identifier (ACC URN where possible, otherwise `ProjectFileInstance`).
3. **Review results** are stored as first-class data, with timestamp and reviewer. The most recent result is authoritative.
4. **Link back to project / task / file** is explicit. A review row references the project, the task that initiated it (if any), and the file under review.
5. **AI assistance, if used, is advisory** (see `Domains\AI\AiSystemPrinciples-2026-05-26.md`). AI never auto-approves or auto-rejects a plan.
6. **Nothing is performed automatically without explicit user confirmation** — including marking a plan as approved/rejected, advancing workflow, or writing back to ACC.
7. Plan Review must integrate with existing tasks/actions; it must not create a parallel workflow engine.

## What we do not do now
- Do not auto-approve or auto-reject plans.
- Do not store review results outside the proper domain tables.
- Do not bypass the workflow dispatcher for plan-related actions.

## Dropped / cancelled / postponed
- Embedding plan review inside generic filing UI — dropped.
- AI auto-decision on plan review — postponed (requires explicit decision).
- Full review schema definition — postponed.

## Relevant terms / search terms
Plan Review, drawing review, reviewer, review result, advisory AI, ACC URN, ProjectFileInstance, task linkage.
