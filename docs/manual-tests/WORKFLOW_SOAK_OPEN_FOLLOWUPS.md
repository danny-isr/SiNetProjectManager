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
| _(none)_ | | | | | |

---

## Parked / deferred (add rows as soak continues)

| Id | Area | Summary | Status |
| --- | --- | --- | --- |
| SOF-005 | System health — Ollama | Timeout strip noise (`OllamaStatusContributor` 5s) | Parked — not blocking Tree A; revisit after Approved |
| SOF-006 | OpenQuote «פתח ב-ACC» | Opens external browser by design | Parked — product choice after Tree A |
| SOF-018 | SendQuote — label on sent message | Operator wants Gmail project label (and/or «אישור שליחה») on the **sent** message, not only SQL `QuoteSendProof` + PDF `QuoteSendDocument`. Labels on Sent are possible via Gmail API; not done today by design (proof = MessageId). | Parked — after Tree A; product/docs first |
| SOF-019 | FollowQuote → skip WO | Option on client approval: «האישור כולל הזמנת עבודה» auto-closes/skips `FollowWorkOrder` on Planning tracks | Parked — product/docs after FollowWorkOrder Email-first ships; see `FOLLOW_WORK_ORDER.md` |
| SOF-020 | PLN WorkOrder → MaterialCheck hang | After `WorkOrderReceived` on Roads task=21: Closure OK, Evaluator matched stage 6→7, but **no** `Engine.Advance` / MAT provision in WF log; UI hung / error; operator retried Complete 4× | Eng: Advance + StartSubWorkflow on `PLN.Execution.MaterialCheck` — surface exception, timeout, deadlock. Reproduce after clean Tree A | **High — blocks Tree C.1** |
| SOF-021b | ProjectWork — open new alt | After blank/template create in ProjectWork tree, expand/select the new alt as active context | Parked — after Email SOF-021 ships | Parked |

---

## Done (moved out of Open)

| Id | Resolved | Notes |
| --- | --- | --- |
| SOF-023 | 2026-08-11 | FollowQuote: tag QuoteClientApproval → auto File/Move/Complete `QuoteApprovedByClient`. See `FOLLOW_QUOTE_APPROVAL.md`. |
| SOF-022 | 2026-08-11 | AutoOnCreate Gmail GetById fallback; FileMaterial task-select `EnsureTaskEmailFiledWhenBoundAsync`. |
| SOF-021 | 2026-08-11 | Email alt ComboBox always invokes create for `CreateNewId` (TwoWay race). ProjectWork open-alt remains SOF-021b. |
| SOF-017 | 2026-08-10 | ZIP file vs folder gate (`:fs.folder:`); AccService upload 400+detail. Soak verify: Move 2/2 → MaterialCheck task=15. See `SOF017_ZIP_FOLDER_MOVE_METADATA.md`. |
| SOF-016 | 2026-08-10 | Tip VersionId on download + AccService tip-version; Move backfills AccVersionId. Needs AccService restart. See `SOAK_FIX_ROUND_2026-08-10.md`. |
| SOF-013 | 2026-08-10 | Task-bound FileToProject skips picker when WorkSurfaceContext.ProjectId set. |
| SOF-015 | 2026-08-10 | Create-alt uses WorkSurface project + clearer prompt-host diagnostics. |
| SOF-014 | 2026-08-10 | Email tag picker orange for IsRequired OutSidData. |
| SOF-011 | 2026-08-10 | Light idle nudge to drain reloadPending (poll remains cross-client safety). |
| SOF-012 | 2026-08-10 | FileMaterial float empty: status bar on work-item window; quoted `rfc822msgid` alone; skip GetById for RFC822 unique ids. Live verify: `Email.Locate rfc822msgid hit` + `task-select ok task=14` strip=2 project=3144 |
| SOF-002 | 2026-07-31 | Office owner/lock files `~$…` skipped in FileServer/ACC/Drive scan (`FileServerSidecarMetadata.ShouldSkipFromScan`). Unit tests Pass. |
| SOF-001 | 2026-07-31 | ProjectWork float + Acc pop-out: `Topmost=false`, Owner=MainWindow; APP_SHELL §10.1. |
| SOF-003 | 2026-07-31 | `PRP.SendQuote` + internal Compose/`IEmailSender` + MessageId proof (G-Policy exception); admin override kept. |
| SOF-004 | 2026-07-31 | `FollowQuoteApproval`→**Email-first** (SendQuote anchor filter) + ProjectWork file fallback; `QuoteClientApproval` PDF gate (`OutSidData=true` for email tag); `QuoteCancelledNoResponse`. See [`FOLLOW_QUOTE_APPROVAL.md`](./FOLLOW_QUOTE_APPROVAL.md). **Soak 3142:** after `SendQuote` task=7 → `PRP.SentFollowUp` + FollowQuote **task=8**; reopen app and open task=8 to verify Email filter/empty-state/«תיוק קובץ». |
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
| 2026-08-11 | Fresh Tree A after reset: project **3146**, instance=1. Path OK through OpenQuote→FileMaterial (Move 3/3)→MaterialCheck→Calculation→**PRP.Preparation task=17** (open). SOF-021 parked (open alt after create). |
| 2026-08-10 | SOF-011 opened from Tree A 2.2 (project 3144): workbench refresh / event vs 30s poll / multi-user. |
| 2026-08-10 | SOF-012 opened: FileMaterial float empty for task=14; Gmail rfc822msgid miss on inbox=1 at AutoOnCreate; no task-select ok in log. |
| 2026-07-31 | SOF-001 recorded from Tree A Preparation (project 3146, task=17 create-from-template). |
| 2026-07-31 | Soak paused at `PRP.SentFollowUp` / task=19. Instance not mid-patched; after code: Seed + re-run Proposal tail. |
| 2026-07-31 | Target approved for SOF-001/003/004 (soak follow-ups plan). |
| 2026-07-31 | Backlog triage: WIP committed `380481f` (SOF-007/008/009). Open cleared. Resume Tree A on project **3147** @ FileMaterial after Seed בסיסי. |
