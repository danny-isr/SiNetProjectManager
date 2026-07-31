# FollowQuoteApproval — Email-first client decision

> **Status:** Target (2026-07-31)  
> **Related:** [`PROPOSAL_WORKFLOW_MANUAL_TEST.md`](./PROPOSAL_WORKFLOW_MANUAL_TEST.md) §2.8, Proposal `PRP.SentFollowUp`

## Principle

**Email-first, no «מייל או קובץ?» gate.** Open the Email workbench filtered by the SendQuote anchor (thread / counterpart). Empty replies → clear empty state + widen search or ProjectWork file path. Task list presence is the reminder (no timed alerts in this slice).

## Open behavior

1. Resolve **SendQuote proof** for the same project (latest `QuoteSendProof`): `GmailMessageId`, `GmailThreadId`, `PrimaryTo`.
2. Open **Email** (not ProjectWork) with project context:
   - Mailbox scope: **All mail**
   - Address filter: `PrimaryTo` when present
   - Prefer rows in `GmailThreadId` when the page loads (client filter / status)
3. **Replies present** — user picks the reply email.
4. **No replies** — empty-state copy + actions:
   - Wait (close; task stays in list)
   - Clear follow-quote filter (pick another email)
   - **תיוק קובץ** → open ProjectWork on the same task (`QuoteClientApproval` gate)

## Approve path

1. From the reply: tag a PDF as catalog **`QuoteClientApproval`** (`OutSidData=true` so email tagging can target it).
2. Ensure a **physical** FileServer PDF exists on that slot (banner **תיוק קובץ בלי מייל** → ProjectWork upload/DnD if tagging alone did not place a file).
3. In ProjectWork (or after file is present), complete with **`QuoteApprovedByClient`** — gated by `HasRequiredPhysicalFile(QuoteClientApproval)`.

## Other results (unchanged)

- `QuoteRejectedByClient` — no PDF
- `QuoteCancelledNoResponse` — confirm, no PDF  
  Available from ProjectWork fallback / task result picker.

## Anchor persistence (SendQuote)

`ProjectAssignmentEvent` `EventType=QuoteSendProof` Note:

`GmailMessageId={id}; Marker={marker}; PrimaryTo={email}`

`EmailThreadId` column = Gmail thread when Reply-All.
