# Workflow soak — open follow-ups

> **Purpose:** Collect UX / product / engineering follow-ups discovered during interactive
> workflow soak **without** implementing them mid-soak.  
> **Related:** [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md),
> [`PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./PROPOSAL_WORKFLOW_MANUAL_TEST.md),
> [`APP_SHELL.md`](../APP_SHELL.md) §10.1 (task surface layout + Topmost policy).

**Rule:** New soak findings go here as **Open**. Implementation only after a separate approval
round (docs → risk → code), not during Tree A happy-path clicks.

---

## Open

| Id | Area | Summary | Observed | Proposed direction | Priority |
| --- | --- | --- | --- | --- | --- |
| *(none)* | | | | | |

---

## Parked / deferred (add rows as soak continues)

| Id | Area | Summary | Status |
| --- | --- | --- | --- |
| *(none yet)* | | | |

---

## Done (moved out of Open)

| Id | Resolved | Notes |
| --- | --- | --- |
| SOF-002 | 2026-07-31 | Office owner/lock files `~$…` skipped in FileServer/ACC/Drive scan (`FileServerSidecarMetadata.ShouldSkipFromScan`). Unit tests Pass. |
| SOF-001 | 2026-07-31 | ProjectWork float + Acc pop-out: `Topmost=false`, Owner=MainWindow; APP_SHELL §10.1. |
| SOF-003 | 2026-07-31 | `PRP.SendQuote` + `SendQuoteToClientDialog` (compose/Sent/admin override); engine test Pass. |
| SOF-004 | 2026-07-31 | `FollowQuoteApproval`→ProjectWork; `QuoteClientApproval` PDF gate; `QuoteCancelledNoResponse`. |

---

## Session notes

| When | Note |
| --- | --- |
| 2026-07-31 | SOF-001 recorded from Tree A Preparation (project 3146, task=17 create-from-template). |
| 2026-07-31 | Soak paused at `PRP.SentFollowUp` / task=19. Instance not mid-patched; after code: Seed + re-run Proposal tail. |
| 2026-07-31 | Target approved for SOF-001/003/004 (soak follow-ups plan). |
