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

Seeded catalog rows with `Code` (e.g. `QuoteEstimate` = «אומדן הצעה» under **תכתובת → ניהול כספי**): editable title/flags OK; do not delete or clear `Code` without an explicit later decision. Seed does **not** create «הצעת מחיר».

---

## 5. Out of scope

- Replacing ProjectWork  
- Physical ACC/FileServer from this window  
- Editing which JobTypes a **project** has (`TypeOfProjectInProject`)
