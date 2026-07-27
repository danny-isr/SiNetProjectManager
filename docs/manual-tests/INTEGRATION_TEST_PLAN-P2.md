# P2 integration test plan

Status: **Pending**

## SQL and schema

- Verify a clean database starts the New System shell after the schema gate passes.
- Verify a schema-gate failure prevents New System startup and preserves the legacy path.
- Verify required migrations are generated and applied by a developer-owned migration workflow.

## ACC

- Verify ACC metadata is written before the SQL cache is updated.
- Verify a failed ACC write does not leave an advanced SQL cache state.
- Verify the ACC local and HTTP modes resolve the same project and folder identifiers.

## Daily sync

- Start live and offline sync processes concurrently; verify only one receives `SiNetDailySync`.
- Terminate a holder process and verify the session-scoped application lock is released.
- Verify `Sync_Lock` remains available for legacy monitoring but is not used as the mutex.
