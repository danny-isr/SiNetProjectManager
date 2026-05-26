# Diagnostics Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for logging, diagnostics, and recovery.
- **Scope:** Structured logs, diagnostic-only mechanisms, recovery / reconciliation, disabled mechanisms tracking, user-facing system status.

## Purpose
Define how the system observes itself, surfaces problems, and recovers — and what must never silently substitute for truth.

## Source of truth
- `Docs\LOGGING.md` — centralized logging conventions.
- This document for cross-cutting diagnostics principles.

## Core principles
1. **Structured logs**: use centralized logging (e.g. `AppLogger`). Avoid scattered `Console.WriteLine` or ad-hoc string concatenation for log lines.
2. **Diagnostic-only mechanisms** (e.g. legacy DOM probes) must be clearly marked disabled and must not be used as business sources of truth.
3. **Recovery / reconciliation** is the only valid way to assert physical existence in ACC. Metadata or DB read failures alone never mark a file as missing.
4. **User-visible status** must reach the UI — important errors are not "logs only".
5. **Tracking removed / cancelled / postponed mechanisms**: each domain Principles document includes a "Dropped / cancelled / postponed" section so disabled paths are not silently revived.
6. **Disabled mechanisms must stay disabled** until an explicit safety review re-enables them.
7. **Always propagate `CancellationToken`** in async paths to support clean shutdown and recovery.

## What we do not do now
- Do not re-enable disabled fallbacks without explicit approval.
- Do not log sensitive secrets.
- Do not invent new diagnostic channels that bypass `AppLogger`.

## Dropped / cancelled / postponed
- DOM-based diagnostic probes as authoritative — dropped.
- DB-only "file exists" diagnostic — dropped.
- Full structured-logging schema / dashboard — postponed.

## Relevant terms / search terms
AppLogger, LOGGING.md, structured logging, reconciliation, AccInboxReconciliationService, MissingInAcc, StaleAccReference, GmailVisibleAttachmentsDomExtractor (disabled), CancellationToken.
