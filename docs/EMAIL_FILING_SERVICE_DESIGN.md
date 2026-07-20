# IEmailFilingService — Application Port Design

> **Status:** Implemented (2026-07-07) — `SqlEmailFilingService` + `GmailEmailModifyService` registered via `AddSiNetEmailWriteSql()`.  
> Related: [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md), [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md)

## Purpose

Provide a single Application-layer port for project filing side effects so WPF never calls legacy `EmailFilingService` or Gmail modify APIs directly.

## Port location

- Interface: `src/SiNet.Application/Email/IEmailFilingService.cs`
- Commands: `src/SiNet.Application/Email/EmailFilingCommands.cs`
- Status port: `src/SiNet.Application/Email/IEmailStatusService.cs`
- Gmail modify: `src/SiNet.Application/Abstractions/Email/IEmailGmailModifyService.cs` → `GmailEmailModifyService`

## Operations

| Method | Command | Legacy seam |
| --- | --- | --- |
| `FileToProjectAsync` | `FileEmailToProjectCommand` | `EmailManagementService.FileToProjectAsync` / `EmailFilingService` |
| `UnfileFromProjectAsync` | `UnfileEmailCommand` | `EmailManagementService.UnfileFromProjectAsync` |
| `SetStatusAsync` | `SetEmailStatusCommand` | `EmailStatusService.SetStatusAsync` |

## Command fields

**FileEmailToProjectCommand:** `InboxMessageId`, `TargetProjectId`, `ActingUserId`, `GmailMessageId`, optional `GmailThreadId`, `InternetMessageId`, optional `TaskId`, optional `TaskResultCode`.

**UnfileEmailCommand:** `InboxMessageId`, `ActingUserId`, `GmailMessageId`, optional `GmailThreadId`, `InternetMessageId`, optional `TaskId`.

**SetEmailStatusCommand:** `GmailMessageId`, `GmailThreadId`, `EmailTriageStatus`, `ActingUserId`, optional `InboxMessageId`, `ThreadUniqueId`.

## Result

`EmailFilingResult(Succeeded, ErrorMessage?, AssignedProjectId?)` — structured failure; no fallback paths.

## Source of truth (mailbox association)

- **Gmail project labels** are the source of truth for “filed to project” (`IsFiledToProject`).
- **Order:** attach/remove Gmail label first; SQL `EmailInboxMessage` / mapping update is best-effort mirror.
- **Compensation:** if SQL sync fails after a label was attached, remove the Gmail label so mailbox truth stays consistent.
- **Forbidden:** treating SQL `ProjectId` alone as filed. See [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) and `EmailSystemPrinciples` §6.6.

## Implementation

1. `SqlEmailFilingService` / `SqlEmailStatusService` in `SiNet.Infrastructure.Sql`
2. Registered in `AddSiNetEmailWriteSql()` (called from `AddSiNet()` composition root)
3. **Wired:** `EmailListViewModel` context menu (file/unfile/status). **Deferred:** viewer action bar buttons in `EmailWindowViewModel` (`ShowDeferredWriteActions`).

## Out of scope

- MoveToProject / ACC filing
- Gmail send / reply from the email viewer
- Direct WPF → `GoogleService` calls

## Companion read port

`IEmailInboxQueryService` (read-only, **implemented**) supports task-driven navigation without filing writes.
