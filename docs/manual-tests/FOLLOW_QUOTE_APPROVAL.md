# FollowQuoteApproval — Email-first client decision

> **Status:** Target (2026-08-11)  
> **Related:** [`PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./PROPOSAL_WORKFLOW_MANUAL_TEST.md) §2.8, Proposal `PRP.SentFollowUp`

## Principle

**Email-first, no «מייל או קובץ?» gate.** Open the Email workbench filtered by the SendQuote anchor (thread / counterpart). Empty replies → clear empty state + widen search or ProjectWork file path. Task list presence is the reminder (no timed alerts in this slice).

**Happy path:** operator selects the client reply → tags **every** taggable attachment to a project file (at least one must be catalog **`QuoteClientApproval`**) → system files (Gmail label if needed), Moves, and **auto-completes** with `QuoteApprovedByClient`.

**Full tagging required:** same as FileMaterial — Move is blocked while any taggable attachment lacks a project-file target. Choosing an alternative alone is not tagging; each attachment needs an explicit tag to a catalog slot.

## Open behavior

1. Resolve **SendQuote proof** for the same project (latest `QuoteSendProof`): `GmailMessageId`, `GmailThreadId`, `PrimaryTo`.
2. Open **Email** (not ProjectWork) with project context:
   - Mailbox scope: **All mail**
   - Address filter: `PrimaryTo` when present
   - Prefer rows in `GmailThreadId` when the page loads (client filter / status)
3. **Replies present** — user picks the reply email. Banner: tag all attachments including אישור לקוח → task completes after Move.
4. **No replies** — empty-state copy + actions:
   - Wait (close; task stays in list)
   - Clear follow-quote filter (pick another email)
   - **תיוק קובץ** → open ProjectWork on the same task (`QuoteClientApproval` gate)

## Approve path (happy path)

1. From the reply: tag **all** taggable attachments; include a PDF as **`QuoteClientApproval`** (`OutSidData=true`). Use a different alternative when two files share the same catalog slot.
2. When every attachment is tagged and QuoteClientApproval is present, the Email surface **automatically**:
   - Ensures Gmail project label (File) when the reply is not yet «משויך»
   - Runs **Move**
   - Calls `CompleteAsync` with **`QuoteApprovedByClient`**
3. If QuoteClientApproval is tagged but siblings are still untagged — status reminds to finish tagging; Move stays blocked.
4. Surface dismisses on `TaskClosed` + workflow advance (same dismiss rules as FileMaterial).

## Other results (fallback — ProjectWork)

- `QuoteRejectedByClient` — no PDF
- `QuoteCancelledNoResponse` — confirm, no PDF  
  Available from banner **תיוק קובץ** / ProjectWork result picker when the operator is not approving via email tag.

## Mailbox association (SoT)

Green «משויך» remains **Gmail project label only** (`docs/EMAIL_ACC_SOURCE_OF_TRUTH.md`). OpenQuote **AutoOnCreate** must attach that label to the originating request email; FileMaterial task-open retries File when SQL is bound but the list row is still Unlinked.

## Anchor persistence (SendQuote)

`ProjectAssignmentEvent` `EventType=QuoteSendProof` Note:

`GmailMessageId={id}; Marker={marker}; PrimaryTo={email}`

`EmailThreadId` column = Gmail thread when Reply-All.
