# IEmailFilingService — Application Port Design

> **Status:** Design only (2026-07-06). **No** Infrastructure implementation until write policy approval.  
> Related: [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md), [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md)

## Purpose

Provide a single Application-layer port for project filing side effects so `EmailWindowViewModel` never calls legacy `EmailFilingService` or Gmail modify APIs directly.

## Port location

- Interface: `src/SiNet.Application/Email/IEmailFilingService.cs`
- Commands: `src/SiNet.Application/Email/EmailFilingCommands.cs`

## Operations

| Method | Command | Legacy seam |
| --- | --- | --- |
| `FileToProjectAsync` | `FileEmailToProjectCommand` | `EmailManagementService.FileToProjectAsync` / `EmailFilingService` |
| `UnfileFromProjectAsync` | `UnfileEmailCommand` | `EmailManagementService.UnfileFromProjectAsync` |

## Command fields

**FileEmailToProjectCommand:** `InboxMessageId`, `TargetProjectId`, `ActingUserId`, optional `TaskId`, optional `TaskResultCode` (for workflow completion bridge after filing).

**UnfileEmailCommand:** `InboxMessageId`, `ActingUserId`, optional `TaskId`.

## Result

`EmailFilingResult(Succeeded, ErrorMessage?, AssignedProjectId?)` — structured failure; no fallback paths.

## Implementation plan (after approval)

1. `SqlEmailFilingService` in `SiNet.Infrastructure.Sql` — thin wrapper over migrated legacy logic
2. Register in composition root only when write policy closes
3. Wire Email window buttons (`LinkToProject`, etc.) through the port — not before

## Out of scope

- MoveToProject / ACC filing
- Gmail send / reply
- Direct WPF → `GoogleService` calls

## Companion read port

`IEmailInboxQueryService` (read-only, **implemented**) supports task-driven navigation without filing writes.
