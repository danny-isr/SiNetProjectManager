# SOF-017 — ZIP Move/Lock misclassification (AccService 500)

> **Status:** Done — verified on soak (Move 2/2 → MaterialCheck)  
> **Related:** [`WORKFLOW_SOAK_OPEN_FOLLOWUPS.md`](./WORKFLOW_SOAK_OPEN_FOLLOWUPS.md) SOF-017, [`FILEMATERIAL_MOVETOPROJECT.md`](../FILEMATERIAL_MOVETOPROJECT.md) § ZIP folder rows

## Problem

On soak (project **3145**, FileMaterial task **14**), tagged `.zip` → Move failed with:

`Failed to write ACC folder move metadata: … 500 (Internal Server Error)` → Move 1/2.

Native ingest stores a `.zip` as a normal **file** (`AccItemId` = lineage). After tip backfill (SOF-016), `AccVersionId` is a tip **file version** URN (`…:fs.file:vf.…?version=1`).

`IsZipContainerAttachment` only required “has AccVersionId + `.zip`”, so Move/Lock used the **folder JSON upload** path with a **file version** as `TargetFolderId` → Autodesk `BAD_INPUT` / “object is not the correct type” → AccService unhandled exception → HTTP 500.

True ZIP **folder** rows (N5 / Legacy) remain: empty `AccItemId` + `AccVersionId` = **folder** URN (`:fs.folder:`). JSON-in-folder for those rows is intentional (no folder custom attributes).

## Target (as-built)

1. Treat ZIP container only when `AccItemId` empty **and** `AccVersionId` is an ACC folder URN (`:fs.folder:`). Align Move loop `isZipFolder`, metadata helpers, and Gmail-recover exclusion.  
2. A `.zip` **file** uses normal item Move/Lock attributes (same as PDF).  
3. AccService `POST …/files/upload` catches upload failures, logs, returns **400** with `{ error, detail }` instead of opaque **500**.  
4. Preserve JSON-in-folder design for real ZIP folders — do **not** invent folder custom attributes.

## Out of scope

- Untag UI for soak workaround  
- Changing QuoteClientRequest catalog tagging policy

## Acceptance

- Retry Move on the same tagged ZIP completes without folder-JSON path (item Move/Lock).  
- Real ZIP folder rows (folder URN) still use JSON sidecar when present.  
- AccService upload Autodesk errors surface as clear 400 detail, not bare 500.
