# Database Identity Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for cross-system identity.
- **Scope:** Identifiers and their authoritative meaning across DB, ACC, Gmail, and Tasks.

## Purpose
Define which identifier is authoritative for each entity, where it lives, and how systems are linked without ambiguity.

## Source of truth (per identity)
| Concept | Authoritative system | Identifier |
| --- | --- | --- |
| Email (business identity) | Gmail / RFC822 headers | **RFC822 `Message-ID`** |
| Email (mailbox-local) | Gmail | Gmail `message.id` (mailbox-scoped, not stable as business identity) |
| Email thread | Gmail | Gmail `ThreadId` (display grouping only) |
| Internal email row | DB | `MessageUniqueId` / `MessageKey` (derived; helper) |
| Project file (logical) | DB | `ProjectFile` |
| Project alternative | DB | `ProjectAlternative` |
| Project file instance | DB | `ProjectFileInstance` (cache/helper for ACC item) |
| File (after upload) | ACC | ACC item URN + custom attributes |
| Task | DB | Task identity (workflow domain) |

## Core principles
1. **RFC822 `Message-ID` is the business identity** of an email. Gmail `message.id` is mailbox-local and must not be used as a stable cross-system identifier.
2. `MessageKey` / `MessageUniqueId` are derived helpers; centralized formatting must remain the single source of truth (`MessageKeyGenerator`-style helpers).
3. After upload, the **ACC item identity (URN) is authoritative** for the file. `ProjectFileInstance` is a cache.
4. DB never alone proves a file still exists in ACC (see ACC principles).
5. Deduplication on import is based on RFC822 `Message-ID` + canonical key, never on Gmail `message.id` alone.
6. `Version` segment in the file naming convention is **not** a version tracker. New files always get `Version = 1`. ACC manages version history natively.
7. Identity values must be set via the canonical creation paths; do not invent identity values in fallback / recovery code.

## What we do not do now
- Do not treat Gmail `message.id` as a permanent business identifier.
- Do not derive ACC viewer URLs from DB identifiers.
- Do not change schema, migrations, ModelSnapshot, `ProjectFileInstance`, or related model layout.
- Do not manually edit EF migration files, Designer.cs, or ModelSnapshot.

## Dropped / cancelled / postponed
- Gmail `message.id` as canonical business identity — dropped.
- New version tracker on filename — dropped (ACC manages versions).
- Cross-domain identity refactor — postponed.

## Relevant terms / search terms
RFC822, Message-ID, rfc822msgid, MessageUniqueId, MessageKey, MessageKeyGenerator, ThreadId, ProjectFile, ProjectAlternative, ProjectFileInstance, ACC URN, dedup.
