# Email («מיילים») — מעקב בדיקה ידנית

> Session resume: 2026-07-19  
> Related chat: [Email window UX + ACC](7d540bca-119e-4d75-9f1a-3aa3aea79697)  
> Runbook: [`docs/manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md`](../../../docs/manual-tests/PROPOSAL_WORKFLOW_MANUAL_TEST.md)  
> Legend: `OK` | `FAIL` | `SKIP` | `AUTO` | `PENDING_LIVE`

## Automated guardrails (this session)

| Check | Result | Evidence |
| --- | --- | --- |
| NewShell menu «מיילים» | AUTO OK | `NewShellEmailMenuTests` |
| Menu hidden when feature denied | AUTO OK | same |
| Factory opens via `IEmailSurfaceHost` | AUTO OK | same |
| Shell close blocked via email host | AUTO OK | same |
| Surface host cache semantics | AUTO OK | same |
| Auto-refresh + ACC background feedback wired | AUTO OK | `EmailManualQaBoundaryTests` |
| Close dialog EmailWindow vs Application | AUTO OK | same |
| CreatePriceQuote + WF-DEBUG + runbook | AUTO OK | same |
| Native CreatePriceQuote engine tests | AUTO OK | `NativeWorkflowEngineTests` (Email_CreatePriceQuote_*) — suite run 2026-07-19 green |

## Wave A — Smoke חלון מיילים

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 1 | תפריט → מיילים בתוכן הראשי | AUTO OK / PENDING_LIVE | NewShell host + menu |
| 2 | Auto-refresh בפתיחה | AUTO OK / PENDING_LIVE | `AutoRefreshOnOpenAsync` |
| 3 | Cache מול surface אחר (ProjectWork) | AUTO OK / PENDING_LIVE | create-once host |
| 4 | מונה תהליכי רקע ACC בסטטוס | AUTO OK / PENDING_LIVE | `BackgroundWorkDisplay` |
| 5 | דיאלוג סגירה «המשך ברקע» | AUTO OK / PENDING_LIVE | `BackgroundCloseScope.EmailWindow` |
| 6 | `EmailBodyPdf Waiting for images` | PENDING_LIVE | צפוי עד timeout; העלאה ממשיכה |

## Wave B — Proposal מ-CreatePriceQuote + `[WF-STEP]`

| # | Item | Status | Notes |
| --- | --- | --- | --- |
| 7 | Seed בסיסי (כלי פיתוח) | PENDING_LIVE | DEBUG menu |
| 8 | Tail `workflow-manual-debug.log` | AUTO OK / PENDING_LIVE | `WorkflowDebugTrace` path |
| 9 | §2.0 CreatePriceQuote → PRP.ProjectSetup | AUTO OK engine / PENDING_LIVE UI | Unit: `Email_CreatePriceQuote_action_starts_native_Proposal_workflow` |
| 10 | §2.2 ProjectOpened → FileMaterial | PENDING_LIVE | runbook checklist unchecked |
| 11 | §2.3 filing → MaterialCheck | PENDING_LIVE | |
| 12 | §2.4–2.8 happy path → PRP.Approved | PENDING_LIVE | |
| 13 | Idempotency CreatePriceQuote | AUTO OK engine / PENDING_LIVE UI | `Email_CreatePriceQuote_action_is_idempotent_per_email` |

## Live session log

| When | Item # | Result | Observation |
| --- | --- | --- | --- |
| 2026-07-19 | guardrails | AUTO OK | Menu/host/UX/WF wiring + engine tests: **15 passed** |
| 2026-07-19 17:52 | CreatePriceQuote | INVESTIGATED | Log: new task **#15** OpenQuoteProject (instance 2); leftover **#14** FileQuoteMaterial from 2026-07-16 still open — not a double-create on one click |
| 2026-07-19 | Task CreatedAt UI | AUTO OK | `ProjectAssignment.Created` (+ StartDate fallback) exposed as `TaskSummaryDto.CreatedAt`; column «נפתחה» on TaskWorkbench |

## How to continue live

1. New System DEBUG → **פרויקטים ותבניות → מיילים** (Wave A #1–2).
2. During ACC upload: confirm status count + close dialog wording (A #4–5).
3. Seed → open `workflow-manual-debug.log` → **פתיחת הצעת מחיר** (Wave B §2.0).
4. Update Status columns to `OK`/`FAIL`; report failures in chat for fixes.
