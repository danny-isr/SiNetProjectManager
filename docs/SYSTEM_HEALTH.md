# System Health / «מצב מערכת»

Status: **implemented.** Eleven legacy checks ported; Drive write probes and MasterPlan Shared Drive
row added after a false-green findings (report generation failed while the panel stayed Idle).

Related: [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
[`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md),
[`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md),
[`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md).

---

## 1. Current state

Two independent mechanisms produce a "system status" list, and they do not overlap.

### 1.1 Legacy stack — `ISystemHealthService`

`SiNetSQL\Services\Health\SystemHealthService.cs` aggregates `IEnumerable<IServiceHealthCheck>`,
runs them with `Task.WhenAll`, coalesces concurrent refreshes per key, and caches results in memory
with no TTL. Eleven checks are registered in `SiNetProjectManagerV2\App.xaml.cs` (lines 663–684).

Surfaces: `SystemHealthIndicator` (popup) and `SystemHealthWindow` (detail), both legacy-only.

### 1.2 New System — `IRuntimeSubsystemStatusService`

`src\SiNet.App.Wpf\Runtime\RuntimeSubsystemStatusService.cs` builds four built-in rows —
`acc`, `gmail`, `acc-ingest`, `workflow-assignees` — plus transient rows from
`IStartupTaskRegistry`, plus legacy rows if and only if an `IExternalHealthCheckSource` is present.

Surface: `SystemStatusWindow` / `SystemStatusView`, plus the footer indicator in `NewShellWindow`.

### 1.3 The gap

`IExternalHealthCheckSource` is registered in exactly one place:

```csharp
// SiNetProjectManagerV2\Services\Composition\NewSystemServiceCollectionExtensions.cs:79
services.AddSingleton<SiNet.Application.Runtime.IExternalHealthCheckSource, LegacySystemHealthCheckSource>();
```

That is the **V2 hybrid** host. The standalone pilot host `SiNet.App.Wpf.exe` does not register it,
and the aggregator resolves it with `GetService` (nullable), so the eleven legacy rows are skipped
silently. The standalone host therefore shows four rows where the legacy host shows eleven.

This was a deliberate scoping decision recorded in `STANDALONE_NEW_SYSTEM_HOST.md` §"Slice 2 — out
of scope". It is not a regression and no check was deleted. It is, however, a **pilot readiness
gap**: the standalone host has no visibility into the database, the file server, the AI endpoint, or
Google Drive folder permissions.

### 1.4 Duplicate contracts (pre-existing defect)

The status contracts exist twice:

| Namespace | State | Contents |
| --- | --- | --- |
| `SiNet.Application.Runtime` | **live** | `IRuntimeSubsystemStatusService`, `SubsystemRuntimeStatus`, `SubsystemRuntimeState`, `IExternalHealthCheckSource` |
| `SiNet.Application.RuntimeStatus` | **dead — no DI registration in any host** | the same three types again, plus `ISubsystemStatusContributor`, `IStartupTaskRegistry` |

`src\SiNet.Application\RuntimeStatus\RuntimeSubsystemStatusService.cs` is an alternative aggregator
that no host builds. The only contract that exists solely in the dead namespace is
`ISubsystemStatusContributor` — which is precisely the extension point this work needs.

---

## 2. Target state

### 2.1 Principle

The standalone host is the pilot host and must show the same operational picture as the legacy host,
**without** referencing `SiNetSQL` or `SiNetProjectManagerV2`. Health checks are therefore ports:
declared in `SiNet.Application`, implemented in the infrastructure project that already owns the
dependency, and registered through the existing composition extensions.

### 2.2 Extension point

`ISubsystemStatusContributor` moves into the live namespace `SiNet.Application.Runtime`:

```csharp
public interface ISubsystemStatusContributor
{
    Task<IReadOnlyList<SubsystemRuntimeStatus>> ContributeAsync(CancellationToken cancellationToken = default);
}
```

Each contributor owns exactly one row and declares its `Key` and `DisplayNameHe` up front, so the
aggregator can render a failure row for it without having to guess an identity.

`RuntimeSubsystemStatusService` takes `IEnumerable<ISubsystemStatusContributor>`, runs them
concurrently during `RefreshAsync` under a per-contributor timeout, and caches the resulting rows for
the synchronous `Rebuild()`.

**Merge order is first-wins by key: external health, then contributors, then built-in rows, then
startup tasks.** Putting the legacy bridge first means the V2 hybrid host keeps rendering exactly the
rows it renders today and its behavior does not change at all; the contributors only surface where no
legacy bridge is registered, which is the standalone host.

A contributor that throws or times out yields a single `Degraded` row for its own key and never
breaks the panel. The exception is logged, never swallowed.

`IExternalHealthCheckSource` **stays** exactly as it is. The V2 hybrid host keeps using it, so the
legacy stack is untouched and nothing is deleted.

The duplicate `SiNet.Application.RuntimeStatus` namespace is marked deprecated with a pointer to the
live namespace. It is **not** deleted in this work; removal requires separate verification that no
host, test, or sibling repo binds to it.

### 2.3 Row inventory

Each ported check becomes one contributor. Ownership follows the existing dependency:

| Key | Row | Project | Backing port (already registered in standalone unless noted) |
| --- | --- | --- | --- |
| `database` | מסד נתונים | `SiNet.Infrastructure.Sql` | `IDbContextFactory<SiNetDbContext>` |
| `file-server` | שרת קבצים | `SiNet.Infrastructure.Sql` | `IFileServerRootResolver` + bounded `Directory.Exists` |
| `ollama` | שרת AI | `SiNet.Infrastructure.Sql` | `ISystemSettingsQueryService` (`OllamaBaseUrl`, `OllamaModel`); requires `AddSiNetAi()` in standalone |
| `google-config` | הגדרות Google | `SiNet.Infrastructure.Google` | `IGoogleClientSecretsPathProvider` |
| `google-account` | חשבון Google | `SiNet.Infrastructure.Google` | `IConnectorAuthService` |
| `InspectionTemplatesFolderId` | תיקיית תבניות בדרייב | `SiNet.Infrastructure.Google` | `IGoogleDriveFolderDiagnostics` — readable + contains spreadsheet |
| `InspectionReportsFolderId` | תיקיית דוחות בדרייב | `SiNet.Infrastructure.Google` | `IGoogleDriveFolderDiagnostics` — **writable** (`capabilities.canAddChildren`) |
| `masterplan-reports-drive` | Shared Drive לדוחות MasterPlan | `SiNet.Infrastructure.Google` | `IGoogleDriveFolderDiagnostics.DiagnoseSharedDriveWriteAsync` — same probe as R01/R02/R03 (`Drives.Get` → `CanAddChildren`) |
| `autodesk-acc` | Autodesk ACC (טוקן) | `SiNet.Infrastructure.Autodesk` | `ITokenProvider` (2-legged probe) |
| `acc-service` | SiOffice.AccService (פנימי) | `SiNet.Infrastructure.Autodesk` | `IAccServiceHealthProbe`, probed in both Local and Remote |
| `google` | Google / Gmail | `SiNet.Infrastructure.Google` | `IConnectorAuthService` + active Gmail probe |
| `workflow` | Workflow Engine | `SiNet.Infrastructure.Sql` | `IDbContextFactory<SiNetDbContext>` |

All eleven legacy checks are ported. Two of them need a note:

- **`workflow`** carries no independent signal in legacy — it reads the `database` row from the
  aggregator. Reproducing that cross-row dependency would couple contributors to each other, so the
  port performs the same connectivity probe directly against the database. The reported state is
  identical; only the wiring differs.
- **`google`** overlaps the existing passive `gmail` row, which reports auth state only. Both rows
  are kept, exactly as the V2 hybrid host shows them today, so the standalone panel matches V2
  row-for-row.

### 2.3.1 Resulting panel

Standalone shows **16 rows**: the 4 built-in rows, the 11 ported legacy rows, plus
`masterplan-reports-drive`. The MasterPlan row is standalone-only (legacy never had it); it exists
because R01/R02/R03 fail with "אין הרשאות כתיבה ל-Shared Drive" when `CanAddChildren` is false, and
the Inspection reports-folder row does **not** cover that Shared Drive id.

### 2.4 Google Drive / Shared Drive diagnostics

`SiNet.Application.Google.IGoogleDriveFolderDiagnostics`:

- `DiagnoseAsync(folderId, expectSpreadsheets, requireWriteAccess)` — folder probe.
  - Templates: readable + at least one spreadsheet (`requireWriteAccess: false`).
  - Inspection reports: readable **and** writable via `capabilities.canAddChildren`
    (`requireWriteAccess: true`). Read-only access is `NoWriteAccess` → panel `Degraded`, not Idle.
- `DiagnoseSharedDriveWriteAsync(sharedDriveId)` — same signal MasterPlan generation uses:
  `Drives.Get(id).Capabilities.CanAddChildren`.

Statuses: `Ok`, `NotConfigured`, `NotAuthenticated`, `NoAccess`, `NoWriteAccess`, `NotFound`,
`InvalidType`, `EmptyFolder`, `Error`. `ReadOnlyOrUnknownWrite` remains only as a fallback when the
API omits capabilities while write was not required.

### 2.5 Behavior requirements

Every contributor must be cancellable, must apply its own bounded timeout, and must never perform
file-system or network I/O on the UI thread. Rows report `LastCheckedUtc` so the panel can show
staleness.

### 2.6 Refresh schedule (standalone)

`RefreshAsync` is **not** window-gated. `NewShellFactory` calls
`IRuntimeSubsystemStatusService.StartPeriodicRefresh()` when the shell is created:

1. **Startup probe** — first full refresh ~3 seconds later, so the footer already reflects real I/O
   before the user opens «מצב מערכת».
2. **Periodic probe** — every **5 minutes** thereafter, so a drive that loses write access mid-day
   surfaces without requiring the user to open the status window.

Concurrent refreshes coalesce (one in-flight at a time). Opening the status window still calls
`RefreshAsync` for an on-demand update; it does not own the schedule. Composition/unit tests that
only resolve the service do **not** start the loop until `StartPeriodicRefresh` is called.

### 2.7 MasterPlan write probe (aligned with generation)

The `masterplan-reports-drive` row must match what R01/R02/R03 actually need before creating files:

1. Shared Drive `Capabilities.CanAddChildren` (same as `NativeReportsDriveHelper.CheckWriteAccessAsync`).
2. **Also** `ReportsRootFolderId` folder `capabilities.canAddChildren` — Shared Drive write does not
   imply write on the configured reports root folder. A green Shared Drive with a read-only root was
   a documented false-green.

Generation itself must check both signals and **log** a write-denied failure (not only return a UI
string), so the next support investigation has a log trail even when the user never opens status.

---

## 3. Startup schema / migration head gate

Standalone host (`SiNet.App.Wpf`) runs `IDatabaseSchemaGate` before the shell opens:

1. SQL `CanConnect`
2. Presence of a small set of required tables (legacy Task Management gate)
3. **No pending EF migrations** — `GetPendingMigrationsAsync` vs migrations in the deployed
   `SiNetSQLDbContext` assembly (cheap `__EFMigrationsHistory` check; not a full column scan)

If pending migrations exist, startup fails closed with the ids and the operator `Update-Database`
command (startup project must be `SiNetProjectManagerV2` — see
[`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md) §4).

The app never auto-applies migrations.

---

## 4. AccService TLS after a clean database

After replacing / wiping the SiNet DB, `SystemSettings` no longer hold `AccService.BaseUrl` or
`AccService.PinnedCertificateThumbprints`. Startup applies host ACC config from DB
(`ApplyAccHostConfigFromSystemSettingsAsync`); without pins, HTTPS to AccService fails with
**SSL connection cannot be established** (self-signed cert).

Operator recovery:

1. Ensure **SiOffice.AccService** is running (typical URL `https://localhost:8443`).
2. **הגדרות → ACC**: set `AccServiceBaseUrl` (e.g. `https://localhost:8443`).
3. Copy the server certificate **thumbprint** into `PinnedCertificateThumbprints`
   (semicolon-separated if multiple). Sources: error text `presented thumbprint=…`, AccService
   diag, or a prior settings backup.
4. **Save**, then **restart** the app (pins bind into `AccServiceControlPlaneOptions` at startup).
5. Open **מצב מערכת** → Refresh; row `acc-service` (SiOffice.AccService) should become ready.

Related rows (do not confuse them):

| Key | Display | Meaning |
| --- | --- | --- |
| `acc` | Autodesk ACC (built-in) | Local vs Remote mode |
| `acc-service` | SiOffice.AccService (פנימי) | AccService health / TLS |
| `autodesk-acc` | Autodesk ACC | Token probe; Degraded («מוגבל») if 2-legged only |

---

## 5. Remediation guidance in «מצב מערכת» (`GuidanceHe`)

When a status is a **known** operator-fixable problem, the detail column shows a second Hebrew
line under the summary: what to do next.

- Catalog: `SystemStatusGuidanceCatalog.Resolve(key, state, summaryHe)` in Application.
- Applied in `SystemStatusRowViewModel.From` (does not require every contributor to set text).
- Initial coverage: `acc`, `acc-service`, `autodesk-acc`, `workflow-assignees`, `gmail`.
- Empty / healthy rows show no guidance line.

Workflow assignee gaps remain **manual** (User Groups + default assignee) — not Seed.

---

## 6. Explicitly out of scope

- Deleting the dead `SiNet.Application.RuntimeStatus` namespace or any legacy check.
- Changing the legacy `SystemHealthIndicator` / `SystemHealthWindow` surfaces.
- Gmail throttle inspection (the legacy `google` check reads it best-effort; the port reports auth
  and reachability only).
- Removing `IExternalHealthCheckSource` from the V2 hybrid host.
- Auto-fixing SSL pins or seeding group memberships.

---

## 7. Risk and complexity

| Area | Assessment |
| --- | --- |
| **Complexity** | Moderate-high. Eleven contributors, one new port with an implementation, one DI change in the aggregator, and registrations across the composition extensions. |
| **Blast radius** | `RuntimeSubsystemStatusService` is shared by the standalone host **and** the V2 hybrid host, so the constructor change affects both. Mitigated by first-wins merge with the legacy bridge ordered first: where a legacy row already exists, the contributor row is dropped, so V2 renders an unchanged set. |
| **Boundary risk** | Low if contributors live in infrastructure projects. High if any check is implemented inside `SiNet.App.Wpf`, which would pull SQL/Google dependencies into the WPF layer and break existing boundary tests. |
| **`AddSiNetAi()` risk** | The Ollama row requires registering AI services in the standalone host, which `STANDALONE_NEW_SYSTEM_HOST.md` currently lists as out of scope. Approved as part of this work; it widens the standalone service graph and that doc needs updating to match. |
| **Runtime risk** | Each contributor performs I/O. Without strict timeouts a slow file server or unreachable Drive can stall the status refresh. Mitigated by per-contributor timeouts and isolated failure rows. |
| **Data/schema** | None. All checks are read-only; no migrations. |
| **Testability** | Good. Contributors are plain async classes with injected ports, testable without WPF. The aggregator merge and de-duplication are unit-testable on the existing STA-free path. |

### Sequencing

Approved as a single sequence: aggregator extension point and merge tests first, then the Drive
diagnostics port and its two rows, then the remaining SQL, Google and Autodesk rows, then the Ollama
row together with `AddSiNetAi()` in the standalone host.
