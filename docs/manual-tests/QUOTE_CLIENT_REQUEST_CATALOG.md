# Catalog — דרישת המזמין להצעת מחיר

> **Status:** Target approved (soak 2026-07-31)  
> **Related:** [`FILE_CATALOG_ADMIN.md`](../FILE_CATALOG_ADMIN.md), Proposal `PRP.FileMaterial`

## Target

| Piece | Value |
| --- | --- |
| Folder path | תכתובת → **ניהול כספי** → **הצעת מחיר** (new subfolder) |
| File code | `QuoteClientRequest` |
| Title | דרישת המזמין להצעת מחיר |
| Type | `.pdf` |
| Required | yes (`IsRequired`) |
| OutSidData | **true** (required so the email ACC tagging picker lists this slot) |
| JobType | חומר כללי |
| Filing | Tag email ACC attachment (PDF) onto this slot during `FileQuoteMaterial` |

Rationale: client/orderer request material for the quote must be filed as a durable required catalog file; ניהול כספי was getting crowded, so quote-request artifacts live under a dedicated «הצעת מחיר» subfolder.

## Notes

- Existing `QuoteEstimate` / `QuoteDocument` / `QuoteClientApproval` remain under **ניהול כספי** (not moved in this slice).
- Seed **creates** folder «הצעת מחיר» under «ניהול כספי» when missing (parent must already exist).
- No EF migration — catalog seed only. Run **טעינת Seed בסיסי** / catalog Ensure after deploy.
