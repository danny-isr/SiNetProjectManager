# Catalog — הצעה_לשליחה (SendQuote PDF)

> **Status:** Target (2026-07-31)  
> **Related:** [`FILE_CATALOG_ADMIN.md`](../FILE_CATALOG_ADMIN.md), Proposal `PRP.SendQuote`

## Target

| Piece | Value |
| --- | --- |
| Folder path | תכתובת → **ניהול_כספי** |
| File code | `QuoteSendDocument` |
| Title | **הצעה_לשליחה** |
| Legacy titles | `הצעת_מחיר_לשליחה`, `הצעת מחיר לשליחה`, `הצעה לשליחה` |
| Type | `.pdf` |
| Required | **no** (`IsRequired=false`) — does not gate task completion |
| OutSidData | **false** |
| JobType | חומר כללי |
| Filing | On SendQuote attach: email attachment is always the filed `QuoteSendDocument` PDF |

## Behavior (SendQuote attach)

1. Open-file dialog: filter **PDF only**; `InitialDirectory` = this project’s **ניהול_כספי** FileServer folder.
2. User picks a PDF. **Original file is never renamed** — FileServer gets a **copy** with the canonical project filename.
3. **If** the selected file is already a physical `QuoteSendDocument` match → do not copy; attach that filed file.
4. **If** no physical `QuoteSendDocument` exists yet → copy as alternative `1` / version `1`, then attach the copy.
5. **If** a physical `QuoteSendDocument` already exists and the user picks a *different* PDF → **do not** auto-add a new version under the same alternative. Prompt for a **new alternative** name (must not collide with existing alternatives). On confirm, copy under that alternative / version `1` and attach the copy. On cancel, do not attach.
6. Email attachment bytes + chip = the **filed** canonical PDF only. Filing failure / cancel blocks attach.
7. Chips: removable (✕); click opens the filed PDF.
8. Physical base-name segment capped by `ProjectFileNameBuilder.MaxBaseNameLength` (**35** as of 2026-07-31 = max `LEN(ProjectFile.Title)` in SIData **33** + 2).

## Notes

- Does not replace `QuoteDocument` (`.docx`) or `QuoteClientApproval` (`.pdf`).
- Seed never overwrites `TemplateLocation`.
- After seed on an existing DB: run **טעינת Seed בסיסי** so Title updates from the old `הצעת_מחיר_לשליחה` alias to `הצעה_לשליחה`.
- Legacy `SiNetSQL.ProjectFileNameBuilder` still truncates at 10; New System Domain builder is authoritative for New System filing.
