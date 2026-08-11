# FollowWorkOrder — Email-first work-order receipt

> **Status:** Implemented (2026-08-10) — verify on soak  
> **Related:** [`FOLLOW_QUOTE_APPROVAL.md`](./FOLLOW_QUOTE_APPROVAL.md), Planning `PLN.WorkOrder`, [`STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./STANDALONE_WORKFLOW_PRODUCTION_GATE.md) Tree C

## Principle

**Email-first, same shell pattern as FollowQuoteApproval.** Open Email filtered by a soft SendQuote anchor (thread / counterpart) when available. Empty list → banner + **«תיוק קובץ בלי מייל»** → ProjectWork. Complete with **`WorkOrderReceived`**. No new catalog slot required in this slice.

Quote client approval ≠ work order. After `PRP.Approved`, each Planning track starts at `PLN.WorkOrder` (`FollowWorkOrder`) before `PLN.Execution.MaterialCheck` (MAT).

## Open behavior

1. Soft anchor (optional): latest project `QuoteSendProof` via existing `IFollowQuoteAnchorResolver` (project from the FollowWorkOrder task) — thread / PrimaryTo preferred, not required.
2. Open **Email** with `EmailHints` (`OfferProjectWorkFallback: true`) so Launcher does **not** block on missing primary TaskLink.
3. Mailbox: All mail; address/thread filters when anchor present.
4. **Empty** — wait / clear filter / **תיוק קובץ בלי מייל**.
5. Banner copy must say **הזמנת עבודה**, not «אישור הצעה».

## Complete path

1. Optional: pick WO email and/or upload files in ProjectWork.
2. Complete with **`WorkOrderReceived`** (no mandatory PDF gate — reuse of existing `QuoteClientApproval` PDF is allowed but not required).
3. Engine AUTO: `PLN.WorkOrder` → `PLN.Execution.MaterialCheck` + StartSubWorkflow (MAT) — already seeded.

## Out of scope (this slice)

- «אישור הצעה כולל הזמנת עבודה» skip of `PLN.WorkOrder` (SOF-019 parked)
- New catalog code `WorkOrderDocument`
- Review continuation start failures (def=6)

## Acceptance

- Click FollowWorkOrder opens Email (not silent block).
- «תיוק קובץ בלי מייל» opens ProjectWork; `WorkOrderReceived` advances to MaterialCheck / MAT child.
