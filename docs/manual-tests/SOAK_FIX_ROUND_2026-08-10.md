# Soak follow-ups — fix round before Tree A restart (2026-08-10)

> **Status:** Implemented (pending operator verify on clean Tree A)  
> **Trigger:** Tree A soak blocked on FileMaterial; operator asked to fix open SOF items then restart clean session.  
> **Related:** [`WORKFLOW_SOAK_OPEN_FOLLOWUPS.md`](./WORKFLOW_SOAK_OPEN_FOLLOWUPS.md), [`FILEMATERIAL_MOVETOPROJECT.md`](../FILEMATERIAL_MOVETOPROJECT.md)

## In scope this round

| Id | Fix |
| --- | --- |
| SOF-016 | Download returns tip `VersionId` (Local + AccService header `X-Acc-Tip-Version-Id`); `GET …/tip-version`; Move backfills `AccVersionId` via `IAccItemService.GetTipVersionIdAsync` before Move/Lock |
| SOF-013 | `FileSelectedEmailAsync`: when `WorkSurfaceContext.ProjectId > 0`, use that project — **no picker** |
| SOF-015 | Create-alternative uses WorkSurface project id; clearer status + WF-STEP when prompt host missing |
| SOF-014 | Email tag FileTreePicker: `IsRequired` OutSidData slots use `SiTreeMissingBrush` (orange) |
| SOF-011 | Light: idle nudge drains `reloadPending` if LoadAsync race left it set |
| SOF-017 | ZIP **file** (AccItemId + tip version) must not use folder JSON Move/Lock; gate on empty AccItemId + `:fs.folder:`; AccService upload failures → 400+detail |

## Out of scope

- Full multi-client realtime task bus (keep 30s poll as cross-machine safety net)

## Restart protocol after ship

1. Rebuild **SiOffice.AutodeskConnector** (sibling) + **SiOffice.AccService** + WPF host  
2. Restart AccService on `https://localhost:8443`  
3. Launch WPF with `SINET_WF_DEBUG=1`  
4. DevTools **איפוס נתוני פיתוח** + Seed verify + groups  
5. Tree A from 2.0 on fresh email  

## Acceptance

- Move of tagged QuoteClientRequest PDF completes when AccItemId exists (VersionId backfilled)  
- Tagged `.zip` **file** uses item Move/Lock (not folder JSON); real ZIP folders (`:fs.folder:`) unchanged  
- FileMaterial «שיוך» uses task project without picker when task context set  
- New alternative prompts for name  
- Required catalog leaf visually distinct in email picker  
- After own task advance, workbench list updates without mandatory manual refresh within one LoadAsync cycle  
