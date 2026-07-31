# Catalog — דרישת_המזמין_להצעת_מחיר

> **Status:** Target (naming fix 2026-07-31)  
> **Related:** [`FILE_CATALOG_ADMIN.md`](../FILE_CATALOG_ADMIN.md), Proposal `PRP.FileMaterial`

## Target

| Piece | Value |
| --- | --- |
| Folder path | תכתובת → **ניהול_כספי** → **הצעת_מחיר** |
| File code | `QuoteClientRequest` |
| Title | דרישת_המזמין_להצעת_מחיר |
| Type | `.pdf` |
| Required | yes (`IsRequired`) |
| OutSidData | **true** (required so the email ACC tagging picker lists this slot) |
| JobType | חומר כללי |
| Filing | Tag email ACC attachment (PDF) onto this slot during `FileQuoteMaterial` |

Rationale: client/orderer request material for the quote must be filed as a durable required catalog file; finance folder was getting crowded, so quote-request artifacts live under a dedicated «הצעת_מחיר» subfolder.

## Naming

Catalog folder/file titles use **underscore instead of space** (office convention). Seed must resolve existing underscore folders first and treat space-separated names as aliases only — never create a parallel `ניהול כספי` / `הצעת מחיר` tree.

## Notes

- Existing `QuoteEstimate` / `QuoteDocument` / `QuoteClientApproval` remain under **ניהול_כספי** (not moved into the nested quote folder).
- Seed **creates** folder «הצעת_מחיר» under «ניהול_כספי» only when missing (parent must already exist).
- Seed never deletes rows and never overwrites `TemplateLocation`.
