# Catalog — הצעת_מחיר_לשליחה (SendQuote PDF)

> **Status:** Target (2026-07-31)  
> **Related:** [`FILE_CATALOG_ADMIN.md`](../FILE_CATALOG_ADMIN.md), Proposal `PRP.SendQuote`

## Target

| Piece | Value |
| --- | --- |
| Folder path | תכתובת → **ניהול_כספי** |
| File code | `QuoteSendDocument` |
| Title | הצעת_מחיר_לשליחה |
| Type | `.pdf` |
| Required | **no** (`IsRequired=false`) — does not gate task completion |
| OutSidData | **false** |
| JobType | חומר כללי |
| Filing | On SendQuote attach: place into this slot **only if** the slot has no physical FileServer file yet |

## Behavior (SendQuote attach)

1. Open-file dialog: filter **PDF only**; `InitialDirectory` = project’s **ניהול_כספי** folder (when resolved).
2. User picks a PDF → attach bytes to the compose draft.
3. **If** any physical file already matches catalog identity `(TypeProjId, Number)` for `QuoteSendDocument` in that folder → **do not** place again.
4. **If** the slot is empty → copy/place the selected PDF under the canonical project filename for `QuoteSendDocument` (FileServer).
5. Physical base-name segment is truncated to 10 chars by `ProjectFileNameBuilder` (catalog `Title` stays full).

## Notes

- Does not replace `QuoteDocument` (`.docx`) or `QuoteClientApproval` (`.pdf`).
- Seed never overwrites `TemplateLocation`.
- After seed on an existing DB: run **טעינת Seed בסיסי** (or restart that runs catalog ensure) so the new `Code` row appears.
