# Outsourcing workflow («מיקור חוץ») — closed-world seed

> **Status:** Approved target (2026-08-02)  
> **Related:** [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md),  
> [`DEV_TOOLS.md`](./DEV_TOOLS.md)

## Purpose

A simple project-bound workflow for work given to an external party:

1. Receive an outsourcing price quote.
2. Approve that quote.
3. Monitor payments manually (remaining balance) — **no payment engine in v1**.
4. Complete.

Definition code: **`Outsourcing`** (stages `OUT.*`).

## Closed-world rules

| Item | v1 |
| --- | --- |
| Definition owner | Seed / DevTools (`SqlWorkflowSeedService`) |
| Visual editor | No |
| JobType mapping in seed | **None** — map later via «מדיניות סוג↔תהליך» (may attach to many JobTypes) |
| Start | Manual from «בריאות תהליכים» → «הפעל תהליך» (active definitions fallback when policy has no rows) |
| Payment integration | Out of scope — `MonitorOutsourcePayments` is a manual task |

## Stages

| Code | Name | Role | Stage task |
| --- | --- | --- | --- |
| `OUT.ReceiveOffer` | קבלת הצעת מחיר מיקור חוץ | Initial | `ReceiveOutsourceQuote` |
| `OUT.ApproveOffer` | אישור הצעת מיקור חוץ | | `ApproveOutsourceQuote` |
| `OUT.MonitorPayments` | מעקב תשלומים | | `MonitorOutsourcePayments` |
| `OUT.Complete` | סיום | Final | (none) |

Transitions: linear `AllRequiredTasksClosed` + Auto between the three work stages into Complete.  
Default assignee group: `OfficeManagement`.

## Apply after code change

1. DEBUG NewShell → **טעינת Seed בסיסי** (seeds TaskTypes including `ReceiveOutsourceQuote` /
   `ApproveOutsourceQuote` / `MonitorOutsourcePayments`, then workflow definitions via
   `SqlWorkflowSeedService` including `Outsourcing`).
2. Open «צפייה בתהליכים (סגור)» / visual canvas — confirm «תהליך מיקור חוץ».
3. «בריאות תהליכים» → **הפעל תהליך** on a test project (if no JobType mapping, the dialog
   lists all active definitions with a warning).
4. Later: attach to JobTypes via «מדיניות סוג↔תהליך» as needed.
