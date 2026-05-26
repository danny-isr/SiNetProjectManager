# Architecture Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for high-level architecture.
- **Scope:** Layering, service boundaries, repositories, and the relationship between UI, DB, ACC, Gmail, and connectors. Cross-cutting principles that do not belong to a single domain.

## Purpose
Describe the shape of the system at a high level so domain documents can reference a single architectural baseline.

## Source of truth
- This document for high-level architecture.
- Domain Principles documents under `Docs\Domains\` for domain-specific rules.

## Core principles
1. **Target framework:** the active TFM is **.NET 10**. Any reference to .NET 8 in older documents is historical.
2. **Layering:**
   - **UI** (`SiNetProjectManagerV2`, WPF + WebView2) — view and view-models only.
   - **Domain / Application services** (`SiNetSQL`) — workflow, tasks, file filing, identity.
   - **Connectors** (`SiOffice.GoogleConnector`, `SiOffice.AutodeskConnector`) — outbound API calls to Gmail and Autodesk.
   - **Privileged service** (`SiOffice.AccService`) — Windows Service for privileged ACC operations.
3. **Service mode boundary:** when `AccService:BaseUrl` is configured, remote WPF clients call the service rather than running local privileged ACC orchestration.
4. **Source-of-truth boundaries:**
   - ACC is authoritative for uploaded files.
   - Gmail (RFC822 `Message-ID`) is authoritative for email identity.
   - DB is authoritative for project structure (`ProjectFile` → `ProjectAlternative` → `ProjectFileInstance`) and a cache/helper for ACC/Gmail state.
5. **Dependency Injection is mandatory** across services and view-models.
6. **No parallel mechanisms:** before adding a service, handler, or storage path, extend an existing one. Duplicates are rejected.
7. **No silent fallbacks:** missing data or failed calls surface visibly (log + UI), they are not papered over.

## What we do not do now
- Do not introduce a new top-level layer or repo without an explicit decision.
- Do not bypass the connector / service boundary from the UI.
- Do not change schema, migrations, ModelSnapshot, or service architecture as part of architecture documentation.

## Dropped / cancelled / postponed
- `.NET 8` as the active TFM — dropped (workspace is `.NET 10`).
- Treating any single legacy document (e.g. `0_SYSTEM_MASTER_SPEC.md`) as the single authoritative spec — dropped; replaced by Domain Principles + Decisions.
- Deep architecture implementation detail in this document — postponed (kept short by design).

## Relevant terms / search terms
Architecture, layering, TFM, .NET 10, SiNetProjectManagerV2, SiNetSQL, SiOffice.AccService, SiOffice.AutodeskConnector, SiOffice.GoogleConnector, service mode, AccService:BaseUrl, source of truth, dependency injection.
