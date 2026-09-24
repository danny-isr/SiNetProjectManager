# Canonical JobType reconciliation

> **Status:** Active
> **Updated:** 24.09.2026
> **Scope:** Move JobType `בדיקה חוות דעת` / `בדיקה_חוות_דעת` to `בדיקה`, and map `בדיקה` to Review and `חוות דעת` to Opinion.

Installing a Release build does not run this. `SqlWorkflowSeedService` is registered only in DEBUG. The Release seed stub throws `NotSupportedException`. Do not open DevTools on a Release build to get around that.

The same rules live in `CanonicalJobTypeReconciliation` and in the DEBUG general seed. Production uses the explicit command below, not the general seed.

## What the command does

Dry-run is the default. It reads JobType titles and reports conflicts. It does not write.

`--apply` writes only when the dry-run conflict list is empty. It then:

1. Renames a lone legacy row to `בדיקה`, keeping the same `JobType.Id`.
2. Creates `בדיקה` or `חוות דעת` only when that exact title is missing.
3. When both the legacy title and `בדיקה` exist, moves references onto `בדיקה` inside one SQL transaction.
4. Ensures the Review and Opinion workflow mappings and the `REV.*` / `OPN.*` stage profiles.

## Conflicts that stop the write

`--apply` throws before any change, and the DEBUG seed leaves that legacy row untouched, when either of these is true:

- The same project has a `Bid` on both JobTypes. Both bids stay, including their amounts. Nothing is deleted.
- Both JobTypes have an Active or Paused workflow for the same project and workflow definition. Project links, mappings, and both instances stay as they were.

`ProjectTypeStatus` and `ProjectTypeTaskType` are copied as new rows and the old rows are removed. Their primary key is not updated in place.

A duplicate project-type link, or a duplicate policy row that already exists on `בדיקה`, is not a bid and is not a reason to stop. The extra link is removed so the pair is not duplicated.

## Operator sequence

1. Back up the production database and restore that backup to a new database name. Do not point the command at production for the first run.
2. Dry-run against the copy:

```powershell
dotnet run --project tools/CanonicalJobTypeReconcile/CanonicalJobTypeReconcile.csproj -- --connection "<copy connection string>"
```

3. If the output contains `Conflict:`, stop. Decide the bids or the two live workflows outside this command. Do not delete a bid to force the merge.
4. When the dry-run prints `Conflicts: 0`, apply on the copy:

```powershell
dotnet run --project tools/CanonicalJobTypeReconcile/CanonicalJobTypeReconcile.csproj -- --connection "<copy connection string>" --apply
```

5. Run the same command again on the copy. The second apply must not create another JobType, mapping, or stage profile.
6. Check the copy: one `בדיקה`, one `חוות דעת`, no legacy title, Review and Opinion mappings enabled, Planning mappings for those two titles disabled, `REV.*` and `OPN.*` profiles present, and the bid and workflow rows from any conflict still unchanged.
7. Only after that check, repeat steps 2 and 4 against production. Take a fresh backup first. This repository change does not run that production step.

There is no EF migration for this. The schema stays as it is.

## Why a project can show no workflow templates

Checked on 24.09.2026 against a fresh copy-only restore `SiNet_TemplateDiag_20260924` of catalog `SIData`. The source catalog was not written. Sample project **3147** (`דניבדיקה 2`) is linked to JobType **20** `בדיקה`.

| Check | Result |
| --- | --- |
| JobType | 20 `בדיקה`, 23 `חוות דעת`. No legacy title. |
| Definitions | PlanningWorkflow id 2 active, Review id 3 active, Opinion id 5 active. |
| Enabled mapping | 20 → Review enabled and default. 20 → PlanningWorkflow present but disabled. 23 → Opinion enabled and default. |
| Stage profile | 20 has 15 active `REV.*` rows and also 12 active `PLN.*` rows. 23 has 8 active `OPN.*` rows. |
| Stage tasks | `REV.ProfessionalReview` (stage 21) has active template TaskType 57 `PerformProfessionalReview`. Most other Review and Opinion stages have templates. `REV.MaterialIntake`, `REV.Completed`, and `OPN.Close` have none; those are a sub-workflow host or a final stage. |
| Opinion projects | 0 rows in `TypeOfProjectInProject` for JobType 23. |
| Old task-type list | JobType 20 still has only TaskType 1 `General` and 3 `PlanReview`. JobType 23 has no `ProjectTypeTaskType` rows. |

A screen that still lists only the Planning workflow for this job type is empty, because that mapping is disabled. The Review task template is present. Renaming the job type does not add it, and it is already there. The old desktop code does not look up the title `בדיקה_חוות_דעת`; that string appears only in seed comments. It keeps using JobType id 20, so the status and task-type rows above still apply. What changes for the old app is the displayed title, and any workflow list that reads enabled mappings now offers Review instead of Planning.

Startup of `SiNet.App.Wpf` runs a read-only check. It does not call `--apply`. If the Review or Opinion mapping, or the current-stage task template, is missing, it shows a warning.

## Apply is one transaction

`ApplyAsync` checks the full plan before it writes. The plan includes a merge of two legacy titles even when `בדיקה` does not exist yet, and it blocks when the Review or Opinion definition, or any of their seeded stages, is missing. The name change, relationship merge, workflow mappings, and stage profiles then run in one SQL transaction. A failure in a later step rolls the rename back. The merge does not open a second transaction when the caller already has one. A conflict does not return success.

## SQL upgrade proof

`CanonicalJobTypeSqlUpgradeTests` creates private LocalDB database `SiNet_JobTypeUpgradeProof`, inserts the old JobType rows, and only then calls `ApplyAsync`. It does not read or write the DEV or production catalog. A restored copy of the live catalog was not used: that catalog was already normalized in an earlier seed, so it no longer holds the old titles.

Run:

```powershell
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --configuration Debug --filter FullyQualifiedName~CanonicalJobTypeSqlUpgradeTests
```

Result on 24.09.2026. The explicit log is `docs/OPS_CANONICAL_JOBTYPE_SQL_PROOF.txt`. If LocalDB cannot be opened, the test fails.

A copy-only backup of catalog `SIData` on `SI-WIN-2K19\SIDATA` was restored to `SiNet_ReconcileCopy_20260924`. The source catalog was not written. On that copy the names were already normalized (`בדיקה` id 20, `חוות דעת` id 23, no legacy title). Dry-run reported `Conflicts: 0` and wrote nothing. Two `--apply` runs both completed with the same ids and no conflicts. Afterward the copy had 2 canonical titles, 0 legacy titles, 23 workflow mappings, and 290 stage-profile rows. The source catalog was not the target of the tool.

Result of the synthetic LocalDB upgrade:

- Two legacy titles and two different bids, with no `בדיקה`: Apply threw, both titles and both bids stayed.
- One legacy title: renamed in place. The same JobType id kept its project link.
- A second legacy title with a status row and a task-type row, and no bid or workflow clash: both rows moved to the keeper. The legacy title was removed.
- A forced failure while inserting workflow mappings, after the rename had been staged: the title returned to `בדיקה_חוות_דעת` and the mapping count stayed 0.
- The next Apply, and a second Apply after it, left the same counts: 2 JobTypes, 2 mappings, 23 stage-profile rows. Review and Opinion were enabled and default.
