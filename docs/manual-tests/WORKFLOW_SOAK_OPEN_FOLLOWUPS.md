# Workflow soak — open follow-ups

> **Purpose:** Collect UX / product / engineering follow-ups discovered during interactive
> workflow soak **without** implementing them mid-soak.  
> **Related:** [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md),
> [`PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./PROPOSAL_WORKFLOW_MANUAL_TEST.md),
> [`APP_SHELL.md`](../APP_SHELL.md) §10.1 (task surface layout + Topmost policy).

**Rule:** New soak findings go here as **Open**. Implementation only after a separate approval
round (docs → risk → code), not during Tree A happy-path clicks.

**Backlog triage (2026-07-31):** No new feature code mid–Tree A. SOF-007/008/009 shipped in
`380481f`. Parked items stay parked until after `PRP.Approved` unless they block the current step.

---

## Open

| Id | Area | Summary | Observed | Proposed direction | Priority |
| --- | --- | --- | --- | --- | --- |
| *(none — Tree A resume)* | | | | | |

---

## Parked / deferred (add rows as soak continues)

| Id | Area | Summary | Status |
| --- | --- | --- | --- |
| SOF-005 | System health — Ollama | Timeout strip noise (`OllamaStatusContributor` 5s) | Parked — not blocking Tree A; revisit after Approved |
| SOF-006 | OpenQuote «פתח ב-ACC» | Opens external browser by design | Parked — product choice after Tree A |

---

## Done (moved out of Open)

| Id | Resolved | Notes |
| --- | --- | --- |
| SOF-002 | 2026-07-31 | Office owner/lock files `~$…` skipped in FileServer/ACC/Drive scan (`FileServerSidecarMetadata.ShouldSkipFromScan`). Unit tests Pass. |
| SOF-001 | 2026-07-31 | ProjectWork float + Acc pop-out: `Topmost=false`, Owner=MainWindow; APP_SHELL §10.1. |
| SOF-003 | 2026-07-31 | `PRP.SendQuote` + internal Compose/`IEmailSender` + MessageId proof (G-Policy exception); admin override kept. |
| SOF-004 | 2026-07-31 | `FollowQuoteApproval`→ProjectWork; `QuoteClientApproval` PDF gate; `QuoteCancelledNoResponse`. |
| SOF-007 | 2026-07-31 | Complementary geometry via `PrepareTaskSurfaceWindow` on task hosts (`380481f`). Verify on next FileMaterial/OpenQuote open. |
| SOF-008 | 2026-07-31 | Catalog `QuoteClientRequest` («דרישת_המזמין_להצעת_מחיר») under תכתובת→ניהול_כספי→הצעת_מחיר; `.pdf`, `OutSidData=true`. Seed uses underscore titles, cleans space-named duplicates, never overwrites `TemplateLocation`. **Seed בסיסי** after deploy. |
| SOF-009 | 2026-07-31 | Process-wide single task work surface (`ITaskSurfaceWindowCoordinator`); Email/Inspection OpenOrRebind; dialogs close floats before ShowDialog. APP_SHELL §10.1. |
| SOF-010 | 2026-07-31 | ProjectWork orange = current-task gate catalog codes only; folders orange when descendant missing even with other physical files. |

---

## After Tree A Approved (queue)

1. Tree B Opinion (`OPN.*`) — gate matrix.
2. SOF-005 / SOF-006 product decisions if still needed.
3. Integrity / Watchdog / Closed viewer (E/F).

---

## Session notes

| When | Note |
| --- | --- |
| 2026-07-31 | SOF-001 recorded from Tree A Preparation (project 3146, task=17 create-from-template). |
| 2026-07-31 | Soak paused at `PRP.SentFollowUp` / task=19. Instance not mid-patched; after code: Seed + re-run Proposal tail. |
| 2026-07-31 | Target approved for SOF-001/003/004 (soak follow-ups plan). |
| 2026-07-31 | Backlog triage: WIP committed `380481f` (SOF-007/008/009). Open cleared. Resume Tree A on project **3147** @ FileMaterial after Seed בסיסי. |
