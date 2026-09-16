# Project Intelligence Catalog

> **Status:** Active picker path = **Facts-only local retrieval**  
> **Date:** 16.09.2026  
> **Scope:** In-memory catalog from picker Project DTOs. No scheduler, no migration, no nightly AI, no historical learning in production.

Related: [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md), [`AI_COMPLETION.md`](./AI_COMPLETION.md), [`PROJECTS.md`](./PROJECTS.md)

## Picker runtime (shipped)

The email filing picker («שייך לפרויקט») recommends from **authoritative SQL facts only**:

- ProjectId, ProjectNumber, Title / canonical name, NameAndNumber / ProjectLabelName
- Place.Title, Company.Title, Active, Status
- JobType remains a weak stored fact and is **not** a picker ranking signal yet

The catalog is built **in memory** from the same `ProjectSummaryDto` list the picker already queries (`ProjectSearchQuery`, `IncludeClosed: false`). No `ProjectIntelligence*` tables.

| Rule | Behavior |
| --- | --- |
| Engine | `IEmailProjectSuggestionService` → `ProjectIntelligenceRetriever` **Facts** layer |
| When | Once, on picker open, from the **cleaned Subject** (not later typed search text) |
| Display | Header **הצעות** (not «הצעות AI»). Prefer Top 3; allow 4–5 only when extra hits are close and also meaningful |
| Empty | If there is no **meaningful match** (strong signal), show **no chips**. Do not force Top 3 |
| Strong signals | Exact project number; exact distinctive name / label; distinctive name phrase (2–3 **non-generic** tokens from Title/Label); Place **plus** a street/plot hint that also appears on that project's facts |
| Weak alone | Place-only, client-only, generic token overlap — **not shown** |
| Click | `SelectProjectCommand` on the **same** DTO instance from the source list. Does not file |
| Search / prefill | Unchanged. User can type, clear, scroll, and select manually |
| Live AI | **Not** on the picker path. No Ollama/Gemini, no «מחפש פרויקטים מתאימים...» |
| Gmail | No mailbox read/write from the picker |

`IEmailProjectRecommendationService` (400-candidate live AI) stays registered for future use and is **not** called by the picker.

`ProjectIntelligenceEnrichmentService` / Observed / AiDerived stay as prototypes. Do not invoke them from the interactive picker until:

- enough **authoritative** historical Subjects exist
- Facts-only retrieval is measurably worse than Facts+Observed / rerank
- no leakage from guessed or backfill associations

### Historical subjects — keep out

`SqlConfirmedProjectSubjectSource` remains isolated (tests / Phase 1 eval only).

**DEV 16.09.2026:** 44 Inbox rows; 9 non-office+subject; 9 Assigned mappings; **ThreadUniqueId clean join 2/9**. That is a data-quality/integration finding, not a picker bug. Do not enable Observed source learning from this volume, and do not “fix” the 2/9 join in the picker slice.

Measured Facts-only retrieval on ~3028 projects: avg ~166 ms, P95 ~176 ms — acceptable in-process. No persistence yet.

## Authority (Phase 1 audit)

### Project facts (SQL `Projects`)

| Concept | Authoritative field | Notes |
| --- | --- | --- |
| ProjectId | `Projects.Id` | |
| ProjectNumber | `Projects.Number` (formatted) | Dummy `0` / `9999` excluded by selector |
| CanonicalName | `Projects.Title` | |
| ProjectLabelName | `Projects.NameAndNumber` | Gmail leaf display cache |
| Place | `Place.Title` via `PlaceId` | City/locality. **No street/address column exists.** |
| Client | `Company.Title` via `CompanyId` | מזמין |
| Active | `EndOfProject != true` | |
| Status | `ProjectStatus.Title` | |
| JobType | first `TypeOfProjectInProjects` title | Weak retrieval signal only |

Do not invent street/planning-area fields. Streets appear only in Observed / AI-derived text.

### Historical subjects (confirmed filing cache)

| | |
| --- | --- |
| **Project source** | `IProjectQueryService` / `Projects` + Place/Company |
| **Historical subject source** | `EmailInboxMessage.Subject` (persisted locally, max 500) |
| **How ProjectId is proven** | Inbox `ProjectId` **equals** `ThreadStatusMapping.ProjectId` for the same `ThreadUniqueId`, mapping `Status = Assigned`, and `ProjectId` is **not** the office-default project (`OfficeManagementProjectId`, typically 136). That pair is written by `SqlEmailFilingService.TrySyncSqlAfterFileAsync` **after** Gmail label attach. |
| **Subject locality** | Local SQL. **No Gmail crawl.** |

**DEV measured 16.09.2026:** 3028 projects; inbox 44 rows; inbox non-office+subject 9; mapping Assigned non-office 9; **clean join 2**. Historical learning is **STOPPED** until more File() traffic exists or an approved mailbox harvest is designed. Do not add a nightly Gmail crawler.

**Not used:** Gmail live labels; DEV-029 Historical Backfill predictions; inbox rows on the default office project; mapping without a matching inbox subject.

## Profile layers

- **Facts** — SQL only  
- **Observed** — cleaned subjects already filed to that ProjectId  
- **AiDerived** — DeepAnalysis aliases/keywords, never presented as facts  

Code: `src/SiNet.Application/ProjectIntelligence/`.

## Runtime rule

Interactive picker must **not** send hundreds of projects to an LLM. Local retrieval first; optional rerank of Top 5–10 only. The current 400-candidate live picker is not expanded and is not committed as the target.

## Nightly job (design only — not implemented)

1. Load active/recent projects.  
2. Recompute `SourceHash` from facts + confirmed observed subjects.  
3. Rebuild only changed profiles.  
4. Call `Ai.DeepAnalysis` only when the hash changed **and** confirmed subjects exist.  
5. Persist a new catalog generation; publish atomically (swap generation id).  
6. On failure keep the last good generation.

| Event | Behavior |
| --- | --- |
| New project | Facts-only profile, no AI until first confirmed subject |
| Rename / Place / Client change | Hash changes → rebuild facts; AI only if observed also exists |
| Closed / reopened | Keep profile; `IsActive` filters retrieval |
| New filed Subject | Append observed; rebuild hash; AI if enrichment stale |
| AI unavailable overnight | Skip AiDerived; keep previous aliases or facts-only |
| Partial failure | Publish only completed profiles; previous generation remains default |

Normal night estimate: AI only for hash-changed projects with confirmed subjects. On current DEV that is ~0–2, not 3000.

## Storage (propose only — no migration)

**Recommended:** SQL tables + optional compact JSON snapshot.

```
ProjectIntelligenceGeneration (GenerationId, PublishedAtUtc, GeneratorVersion, IsCurrent)
ProjectIntelligenceProfile (GenerationId, ProjectId, SourceHash, NormalizedSearchText, FactsJson, AiJson, GeneratedAtUtc, AiProvider, AiModel)
ProjectIntelligenceObservedSubject (GenerationId, ProjectId, CleanedSubject, FirstSeenUtc)
```

| Concern | SQL + JSON snapshot |
| --- | --- |
| Reliability | Last good generation stays queryable |
| Querying | By ProjectId / SourceHash |
| Deployment | No file-share ownership fights |
| Incremental | Hash compare |
| Provenance | Provider/model/version columns |
| Rollback | Flip `IsCurrent` |
| Multi-user | Shared SQL |

Migration required later: **YES** (not now).

## Privacy

DeepAnalysis prompt = facts + confirmed Subjects only. No bodies, attachments, or recipients. Prototype settings: `Ai.DeepAnalysis` = **Ollama / gemma3:27b**.
