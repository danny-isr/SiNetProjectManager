# New System — Limited Production Pilot Envelope

> **Status:** Active (2026-07-05)  
> **Scope:** Defines what V2 New System mode may expose in a **limited production pilot**. This is
> **not** full legacy replacement, **not** broad window migration, and **not** a production switch
> away from `GoogleService` / legacy email management.
>
> Related:
> [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) (G-Startup closed; G-Policy pending),
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),
> [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md).

---

## 1. Pilot intent

V2 can launch **New System** (`RunNewSystemStartup` → `NewShellWindow`) as a **controlled production
pilot** when:

- Gmail silent restore runs at startup (G-Startup ✅).
- Only **read-only / operator** surfaces are menu-exposed.
- **No** stub or deferred action appears as an active production button.
- **No** harness window is exposed to regular users in release builds.
- Legacy production paths (`MainWindow`, `GoogleService`, `EmailManagementView`) remain unchanged.

---

## 2. Allowed in production pilot (now)

| Surface | Mode | Notes |
| --- | --- | --- |
| **EmailWindowView** | Read-only Gmail | Summaries, body/details, attachment **metadata** only. Deferred write/workflow actions **hidden** and **disabled**. |
| **AccControlPlaneStatusWindow** | Read-only / control-plane | Mode, health, diagnostics, browse, read-only reconciliation. No upload/provisioning UI. |
| **SecretSetupWindow** | Native admin | Credential vault / keys (admin feature gate). |
| **SettingsWindow** | Native | Personal + system admin settings (feature gates). |
| **UserListWindow / AddUserDialogWindow** | Native admin | User management (admin gate). |
| **ActionPermissionsWindow** | Native admin | Action permissions (admin gate). |
| **NewShellWindow** | Host | Project selector + menu only; not a workflow actor. |

**Gmail read scope (allowed):**

- Silent restore via `IConnectorAuthService.TryRestoreSessionAsync` at V2 New System startup.
- Explicit connect (`Connect`) when session missing.
- Refresh / search / load details.

**ACC read scope (allowed):**

- Control-plane display, catalog/discovery browse, item lookup, read-only inbox reconciliation display.

---

## 3. Dev-only / harness-only (not production menu)

| Surface | Why | Dev entry point |
| --- | --- | --- |
| **InspectionShellView** | Developer harness; task-mode pilot incomplete | V2 legacy `MainWindow` admin preview (`OpenInspectionFromTask_Click`); **DEBUG** shell menu only |
| **InspectionWindowView** | Visual clone with fake/design data + stub commands | Not in production shell menu |
| **InboxViewModel / standalone `SiNet.App.Wpf` MainWindow** | Scaffold harness | Not V2 production entry |
| **Legacy `EmailManagementView`** | Full production email (legacy) | Legacy `MainWindow` only — unchanged |

---

## 4. Deferred / blocked until policy slice

| Area | Blocker | Status |
| --- | --- | --- |
| GmailSend / Reply / Forward | G-Policy | **Suspended** — not wired in New System WPF |
| Gmail modify / labels | Policy | **Deferred** |
| Attachment open/download | Read slice gap | **Deferred** — metadata only in pilot |
| Email filing / MoveToProject / task completion from Email window | Workflow/filing slice + coordinator path | **Deferred** |
| Drive / Sheets / Reports | Legacy/deferred | **Do not expose** |
| ACC upload / provisioning / metadata write / folder ensure UI | ACC-Write-Policy | **Blocked** |
| Production `GoogleService` switch | Host switch decision | **Blocked** |
| TaskPanel / FloatingProjectTasks / WorkflowDashboard in New Shell | Not migrated | **Deferred** |
| Broad task-aware window migration | Integration contract exists; pilot not started | **Deferred** |

---

## 5. Production shell menu rules

Implemented in `NewShellFactory.BuildMigratedOnlyMenu()`:

| Menu item | Production (Release) | DEBUG |
| --- | --- | --- |
| Email (read-only) | ✅ when `Shell.OpenEmailSurface` | ✅ |
| Inspection harness | ❌ hidden | ✅ when `Shell.OpenInspectionSurface` |
| Native admin / ACC status / settings | ✅ per feature codes | ✅ |

**No new feature-flag framework.** Temporary `#if DEBUG` guard for harness menu only; email/admin items
use existing `AppFeatureCodes` + `IAuthorizationQueryService`.

---

## 6. EmailWindowView production guardrails

| Rule | Implementation |
| --- | --- |
| No stub buttons visible | `ShowDeferredWriteActions == false` hides write/workflow UI |
| Deferred commands disabled | `DeferredProductionPilotAction` → `CanExecute` always `false` |
| Code preserved | Commands remain for future slices — **not deleted** |
| Title / menu | "ניהול דואר — קריאה בלבד" |
| Visual placeholders hidden | `ShowDeferredVisualPlaceholders == false` hides pagination (`1 / 3`), calendar, help, date pickers |
| Clear search | `ClearSearchCommand` clears `SearchText` and reloads (real, minimal) |
| Production notice | `ProductionPilotNotice` in sidebar + viewer footer (replaces "שלד ויזואלי" copy) |
| Unread badge | Shown only when `ShowUnreadCount` (`UnreadEmailCount > 0`) |

Deferred actions (hidden + disabled): LinkToProject, CreateTaskFromEmail, MarkHandled, Archive,
Reply, Forward, OpenAttachment, CompleteTask.

**Still suspended (markup retained, hidden/disabled):** real pagination, Gmail date-range filtering,
calendar integration, help system. No new `IEmailGateway` query wiring in this polish slice.

---

## 7. Workflow / Task guardrails (production)

- ViewModels **must not** mutate `WorkflowStage` or `ProjectStatus` directly.
- Business task completion **must not** run from New System email/inspection surfaces in this pilot.
- Task open path (when added later): `TaskNavigationResolver` / `ITaskNavigationService` only — **no new router**.
- No first/last entity fallback when work target missing.

See [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md).

---

## 8. ACC host production checklist

ACC read/operator surfaces in New System **require V2 host** registration. Before expanding ACC beyond
status/operator read-only:

| Check | V2 host |
| --- | --- |
| `AddSiNetAutodesk()` in New System graph | ✅ `NewSystemServiceCollectionExtensions` |
| `AccService:BaseUrl` / mode via `IAccServiceModeProvider` | ✅ DB setting + config |
| API key / vault via `VaultAccServiceKeyDiagnostics` | ✅ Secret Setup |
| `ITokenProvider` for local mode | ✅ V2 `App.xaml.cs` host glue |
| `LegacyHostLocalAccInboxBootstrapExecutor` | ✅ host-only privileged local bootstrap |
| `IAccInboxReconciliationService` impl | Legacy `SiNetSQL` — port native, impl host-bound |
| Standalone `SiNet.App.Wpf` harness alone | ❌ incomplete ACC — not production representative |
| ACC write/upload/provisioning from New System WPF | ❌ **blocked** until ACC-Write-Policy |

**ACC-Host contract:** partially closed for read/operator; **not** closed for write/provisioning.

---

## 9. Verification (smoke)

```powershell
dotnet build SiNetProjectManagerV2/SiNetProjectManagerV2.csproj
dotnet test src/SiNet.App.Wpf.Tests/SiNet.App.Wpf.Tests.csproj
```

Key test classes:

- `ProductionPilotBoundaryTests`
- `GoogleFoundationClosureTests`
- `NewSystemBoundaryTests`
- `WorkSurfaceWorkflowIntegrationBoundaryTests`

### 9.2 Limited production smoke (2026-07-05)

| Field | Value |
| --- | --- |
| **Smoke status** | **Partial** — automated passed; interactive UI smoke requires manual run |
| **Date** | 2026-07-05 |
| **Environment** | Local workspace `d:\repos2026\SiNetProjectManager_GitHub`; Debug + Release build |
| **User profile** | Agent/automated (no authenticated DB/Gmail session in smoke run) |
| **Git** | `git.exe` not on PATH; verified via VS bundled git + GitHub API — see §9.3 |
| **Build** | ✅ Debug + Release — 0 errors (263–274 pre-existing warnings, unrelated to pilot) |
| **Tests** | ✅ 955/955 `SiNet.App.Wpf.Tests`; ✅ 174/174 boundary filter |
| **Process launch** | ✅ V2 exe starts (brief run); full New System path not exercised to shell (requires mode/vault/DB UI) |

**Automated / static verification (passed):**

- `RunNewSystemStartup` → vault → DB → DI → `ServiceLocator.Initialize` → `StartNewSystemConnectorAuthRestore` → `LaunchNewSystemShell` (no Legacy fallback on failure).
- G-Startup: `TryRestoreSessionAsync` only — no `LoginAsync` in restore path.
- NewShell menu: Email `"דוא\"ל — קריאה בלבד"`; Inspection wrapped in `#if DEBUG`; no ProjectWork/Reports/Drive/Sheets/WorkflowDashboard.
- Email pilot: `ShowDeferredWriteActions` / `ShowDeferredVisualPlaceholders` false; `ProductionPilotNotice`; `ClearSearchCommand`; no `OpenAttachmentCommand` in XAML.
- Release build succeeds (Inspection menu excluded from Release compilation).

**Manual smoke still required (operator checklist):**

1. Select **New System** at startup; confirm **NewShellWindow** opens and **MainWindow** does not.
2. Open Email — verify read-only UI, Connect/Refresh/Search/Clear, summaries/body/attachment metadata (with valid project + Gmail token).
3. Open ACC status — mode/health/diagnostics; read-only browse/reconciliation if data exists; confirm no upload/provisioning.
4. Open Secret Setup / Settings / User Admin / Permissions per role.

**Known issues:**

- Interactive runtime smoke (New System path → Email/Gmail/ACC/admin) **not completed** in automated/agent run — requires operator with DB/vault/Gmail session (§9.3).

**Decision:** **Needs manual interactive smoke** before limited users. After operator passes checklist in §9.3 → **ready for 1–2 internal read-only pilot users**.

### 9.3 Interactive smoke gate (2026-07-05)

| Field | Value |
| --- | --- |
| **Interactive smoke status** | **Blocked** — operator run required |
| **Date** | 2026-07-05 |
| **Environment** | `d:\repos2026\SiNetProjectManager_GitHub`; Windows; Debug + Release build |
| **Branch / commit** | `SiWorkNet10` @ `9250586` (`docs(readiness): add 9.2 limited production smoke section`) — local clean, in sync with `origin/SiWorkNet10` |
| **User profile** | Agent/automated — no interactive DB/Gmail/ACC session |
| **DB/vault status** | Not exercised (requires operator UI) |
| **Gmail status** | Not exercised (requires operator + valid token/project) |
| **ACC status** | Not exercised (requires V2 host + AccService config) |
| **Build** | ✅ Debug + Release — 0 errors (~259–263 pre-existing warnings) |
| **Tests** | ✅ 955/955; ✅ 174/174 boundary filter |

**GitHub / file audit (passed):**

All required paths verified on [`SiWorkNet10`](https://github.com/danny-isr/SiNetProjectManager/tree/SiWorkNet10) via GitHub API + local disk:

- `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` ✅
- `src/SiNet.App.Wpf.Tests/Boundary/ProductionPilotBoundaryTests.cs` ✅ (includes visual-placeholder guards)
- `src/SiNet.App.Wpf/Shell/NewShellFactory.cs` ✅
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` ✅
- `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml` ✅ (`ShowDeferredVisualPlaceholders`, `ClearSearchCommand`, `ProductionPilotNotice`)
- `docs/UI_WINDOW_MIGRATION_MAP.md` ✅
- `docs/NEW_SYSTEM_BOUNDARY.md` ✅
- `docs/ACC_CONTROL_PLANE.md` ✅

**Automated re-check (passed):** build, tests, Release compilation (Inspection menu excluded), V2 process launch (brief).

**Interactive checklist — operator must complete:**

| Step | Check | Agent result |
| --- | --- | --- |
| Startup | Mode selection → New System → `RunNewSystemStartup` → NewShell (not MainWindow); no Legacy fallback | ⚠️ Not verified |
| Startup | Vault + DB; clear error if missing; `StartNewSystemConnectorAuthRestore` silent (no auto login) | ⚠️ Not verified |
| Shell/menu | `"דוא\"ל — קריאה בלבד"`; no Inspection in Release; admin items per `AppFeatureCodes` | ⚠️ Not verified (Release build OK statically) |
| Email | Read-only UI; Connect/Refresh/Search/Clear; summaries/body/attachment metadata; no write buttons | ⚠️ **Blocked by missing Gmail/project data** in agent run |
| ACC | Mode/health/diagnostics; read-only browse/reconciliation; no upload/provisioning | ⚠️ Not verified |
| Admin | Native SecretSetup / Settings / Users / Permissions; no legacy admin windows | ⚠️ Not verified |

**Known issues:**

- Agent environment cannot drive WPF mode/vault/DB dialogs or authenticate Gmail/ACC.
- No startup logs found under `%LOCALAPPDATA%\SiNet\SiNetProjectManagerV2\Logs` (startup path not fully exercised).

**Decision:** **Blocked by environment/config** — **not ready** for pilot users until operator completes interactive checklist above and records Pass/Fail per step.

When all steps pass → **Ready for 1–2 internal read-only pilot users only.**

**Next doc slice (after interactive pass):** Email Composite Work Surface Contract — component breakdown before composite implementation; **no business logic in smoke gate task.**

### 9.1 SiWorkNet10 file checklist (audit)

These paths must exist on branch `SiWorkNet10` for the production pilot envelope to be considered
**closed on GitHub** (not only in a local workspace):

| Path | Purpose |
| --- | --- |
| `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` | This envelope |
| `src/SiNet.App.Wpf.Tests/Boundary/ProductionPilotBoundaryTests.cs` | Pilot guard tests |
| `src/SiNet.App.Wpf/Shell/NewShellFactory.cs` | Email read-only menu; Inspection `#if DEBUG` only |
| `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs` | `ShowDeferredWriteActions`, disabled deferred commands |
| `src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml` | Hidden write UI; read-only attachment chips |
| `docs/ACC_CONTROL_PLANE.md` | §5.1 Production host checklist |
| `docs/NEW_SYSTEM_BOUNDARY.md` | Cross-ref to this doc |

---

## 10. Recommended next production slice

**Email read-only production polish** — **closed** in this slice: visual placeholders hidden/disabled,
production-friendly notice copy, `ClearSearchCommand`, Hebrew empty state, unread badge when count > 0.
No write/send/workflow wiring.

**Option B (higher value, higher risk):** Email filing task-aware slice — only via existing
`MoveToProject` → handler → `ITaskCompletionCoordinator` path; no new ACC write from WPF.

**Do not** start with Option B until G-Policy + filing slice are explicitly approved.

**After polish:** run real smoke on V2 New System with limited users before expanding pilot audience.

---

## 11. Suspended / not deleted

| Item | Status |
| --- | --- |
| Full production replacement | **Suspended** |
| Broad legacy window migration | **Suspended** |
| InspectionShellView in production menu | **Suspended** (DEBUG/dev only) |
| Stub visual-clone actions | **Hidden/disabled**, code retained |
| Email visual placeholders (pagination, calendar, help, dates) | **Hidden/disabled**, markup retained — real integration deferred |
| GmailSend / Drive / Sheets / ACC write | **Suspended** |
| Legacy windows / GoogleService / old tasking model | **Retained** — not deleted |
