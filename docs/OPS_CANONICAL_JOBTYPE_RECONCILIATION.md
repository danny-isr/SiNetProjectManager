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
