# File Catalog Admin («ניהול קבצים»)

> **Status:** Implemented (New System) — full admin parity with legacy FileManager  
> **Date:** 2026-07-30  
> **Branch:** `SiWorkNet10`  
> **Legacy reference:** `FileManagerView` + `FileManagerViewModel` (V2 menu «ניהול קבצים», Administrator)  
> **New surface:** `src/SiNet.App.Wpf/Admin/FileCatalog/` · menu **מנהלה → ניהול קבצים** · feature `Shell.OpenFileCatalogAdmin`

Related: [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md), [`APP_SHELL.md`](./APP_SHELL.md).

---

## 1. What this is

An **Administrator** window for the **global file/folder catalog** in the DB — the same job as today’s V2 «ניהול קבצים»:

- Filter by JobType
- Browse the global folder tree
- Create folders (sub-folders)
- Create / edit / delete file definitions (`ProjectFile`)
- Assign a file definition to a folder
- Save changes
- Optional: Add / rename JobType (as in V2)

This is **not** «בעבודה 2» (project file tree + physical files). Same data catalog, different screen and purpose — like V2 today.

---

## 2. Principles

1. Native New System window (visual/behavioral clone of V2 FileManager). Do **not** host the legacy UserControl; do **not** grow `FileManagerViewModel`.
2. DI + Application services (no `new DbContext()` in UI).
3. Menu: **מנהלה** → «ניהול קבצים», Administrator only (same as V2 `IsInAdmins`).
4. DB catalog only — no ACC / FileServer physical ops (same as V2 FileManager).
5. Keep V2 screen until New is usable; do not delete legacy.
6. May **add** fields V2 lacks if useful (e.g. show `Code` / `IsRequired`) without redesigning the layout.
7. No EF migrations unless a separate approved schema change is required.

---

## 3. UX

Same layout as V2: top JobType bar, folder tree, files grid, create folder / create file / assign / save. Hebrew labels + shell theme OK.

Folder filter (New System): left-click **תיקיית הפרויקט** → show all files; left-click a specific folder → only that folder’s files. Right-click assign does **not** change the filter. Grid shows a read-only **תיקייה** column. Right-click **מחק תיקייה** only when the folder is empty (no child folders / no file defs); project-root folders cannot be deleted.

Host: floating admin `Window` (like Users / Action Permissions).

---

## 4. Delivery

Implement as one feature toward **full V2 parity** (create folders + files + assign + save). Internal order for safety:

1. Window + menu + load/browse  
2. Create folder, create/edit/delete file defs, assign, save  
3. JobType add/rename if still needed for day-to-day use  

### Catalog naming convention (folders + file defs)

Office catalog titles use **underscore instead of space** between words (FileServer / ACC path parity). Examples: `ניהול_כספי`, `הצעת_מחיר`, `אומדן_הצעה`. Single-word titles stay as-is (`תכתובת`).

Seeded catalog rows with `Code` (JobType חומר כללי) — **canonical titles**:

| Code | Title | Folder | Type | Required | Notes |
| --- | --- | --- | --- | --- | --- |
| `QuoteEstimate` | אומדן_הצעה | תכתובת → ניהול_כספי | `.xlsx` | yes | Gates `PrepareQuoteCalculation` |
| `QuoteDocument` | הצעת_מחיר | תכתובת → ניהול_כספי | `.docx` | yes | Gates `PrepareQuoteDocument`; `OutSidData=false`. Set `TemplateLocation` for «אלטרנטיבה מתבנית» |
| `QuoteClientApproval` | אישור_לקוח_להצעה | תכתובת → ניהול_כספי | `.pdf` | yes | Gates `FollowQuoteApproval` approve; `OutSidData=true` so email tagging can target it (FollowQuote Email-first). Physical FileServer/ACC file still required before `QuoteApprovedByClient` |
| `QuoteClientRequest` | דרישת_המזמין_להצעת_מחיר | תכתובת → ניהול_כספי → **הצעת_מחיר** | `.pdf` | yes | `OutSidData=true` so email ACC tagging can target it during `FileQuoteMaterial`. See [`QUOTE_CLIENT_REQUEST_CATALOG.md`](./manual-tests/QUOTE_CLIENT_REQUEST_CATALOG.md) |
| `QuoteSendDocument` | הצעה_לשליחה | תכתובת → ניהול_כספי | `.pdf` | **no** | SendQuote attach: always file/attach as this slot. Physical base-name cap = max DB title length + 2 (currently 35). See [`QUOTE_SEND_DOCUMENT_CATALOG.md`](./manual-tests/QUOTE_SEND_DOCUMENT_CATALOG.md) |

### Seed rules (`ProjectFileCatalogSeedData`)

1. **Prefer existing** underscore catalog folders/files. Treat space-separated titles as aliases (`ניהול_כספי` ↔ `ניהול כספי`). Prefer the row that already has `TemplateLocation` when reclaiming a `Code`.
2. **Never invent a missing parent** (e.g. do not create `תכתובת`). Create a **child** folder only when the parent exists and no alias of the child is found — always with the **underscore** canonical name.
3. **Never overwrite/clear `TemplateLocation`.** Admin owns templates. When reclaiming `Code` onto a preferred row, **copy** `TemplateLocation` from the discarded duplicate if the keeper lacks one. Cleanup must **not** delete any row that still has a non-empty `TemplateLocation` (merge onto keeper first, or skip).
4. Attach/update by `Code` when possible; rename only known legacy/alias titles to the canonical underscore form; do not overwrite arbitrary admin renames.
5. **Cleanup:** after ensure, delete spurious **space-named** duplicate file defs (never templated leftovers) and empty duplicate folders that match known catalog alias titles. Does not delete the keeper row for each `Code`.
6. Admin **may delete** catalog file defs (including those with `Code`) from «ניהול קבצים» after an extra confirmation. To restore seeded slots, run **טעינת Seed בסיסי** again.

Editable title/flags OK; do not clear `Code` without an explicit decision (delete of the whole def is allowed with confirm).

---

## 5. Out of scope

- Replacing ProjectWork  
- Physical ACC/FileServer from this window  
- Editing which JobTypes a **project** has (`TypeOfProjectInProject`)
