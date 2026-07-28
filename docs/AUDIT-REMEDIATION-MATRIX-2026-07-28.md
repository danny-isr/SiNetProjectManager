# Audit remediation matrix — round 2 (2026-07-28)

Response to **SI Net Project Manager V2 — Remediation Verification Audit** (2026-07-27), which
re-checked the original architecture audit at HEAD `22e7458` and scored 26 findings as
10 fixed / 6 partial / 10 open.

This document records what round 2 changed, what is still open, and — importantly — **what could
not be verified and why**. It is the companion status page to
[`GITHUB-REMEDIATION-BOARD.md`](./GITHUB-REMEDIATION-BOARD.md).

## Status vocabulary

| Status | Meaning |
| --- | --- |
| **Fixed** | Code/config changed and verified by a build, a test, or a script run recorded below |
| **Partial** | Materially improved, with a named remainder and a documented reason |
| **Manual Pending** | Tooling delivered; the action itself needs an operator with live-system access. **Not Run** |
| **Open** | Not addressed this round; lives in `P2-TECH-DEBT-BACKLOG.md` |

## 1. The 26 audit findings

| # | Finding | Before | Now | Evidence |
| --- | --- | --- | --- | --- |
| 1 | MasterPlan API key in tracked `appsettings.json` | Fixed | **Fixed** | `build/secret-scan.ps1` scanned 1794 committable files, 0 hits |
| 2 | Rotate/revoke the leaked key, purge history | Open | **Manual Pending** | Decision: **no history rewrite**. Rotation/revoke unperformed — `OPS-P0-SECRET-ROTATION.md` |
| 3 | `/v1/acc/diag` reachable without an API key | Fixed | **Fixed** | Unchanged this round; only `/health` bypasses `ApiKeyMiddleware` |
| 4 | `/diag` leaked Windows user, key hash/length, exception bodies | Fixed | **Fixed** | Round 2 also removed the fingerprints from the *logs* — see 4.3 below |
| 5 | ACC certificate: weak production fallback, hardcoded password | Fixed | **Fixed** | Unchanged this round |
| 6 | Broad TLS trust for `192.168.*` / `.si-eng.local` | Partial | **Fixed** | Pins now bound in both composition roots; `AccControlPlaneTlsWiringTests` |
| 7 | Old SQL scripts must stop being treated as recovery | Fixed | **Fixed** | Freeze document unchanged |
| 8 | Current DB baseline + migration history + restore rehearsal | Open | **Manual Pending** | `scripts/db/backup-baseline.ps1`, `restore-rehearsal.ps1`, checklist — **Not Run** |
| 9 | Replica baseline missing `MP_TimeHourReports` / `MP_ProjectHoursExtended` | Open | **Manual Pending** | `scripts/db/apply-replica-migrations.ps1` + `dbo.SchemaVersions` — **Not Run** |
| 10 | `AUTO_CLOSE ON`, RCSI unverified | Open | **Manual Pending** | `scripts/db/check-database-settings.ps1` — **Not Run** |
| 11 | Race in the Sync lock | Fixed | **Fixed** | Unchanged this round |
| 12 | No GitHub Actions | Partial | **Fixed (build graph)** | Workflow fetches pinned siblings before restore. **CI is still red on 7 pre-existing test failures** — see §3 |
| 13 | A clean clone cannot be built | Open | **Fixed** | Clean-checkout simulation restored and built Release with 0 errors — §2 |
| 14 | Sibling dependencies not pinned | Open | **Fixed** | `build/sibling-pins.json` holds three real 40-char SHAs; `fetch-siblings.ps1` fails closed |
| 15 | `MasterPlan.SyncEngine.csproj` bloated/corrupt | Fixed | **Fixed** | Unchanged this round |
| 16 | `credentials.json` unconditional `Content` item | Fixed | **Fixed** | Unchanged this round |
| 17 | Package version spread, no `global.json`/lock files | Partial | **Fixed** | `global.json` pins SDK 10.0.301; `NuGet.config` pins the feed; CPM verified. Lock files **declined with rationale** |
| 18 | `Infrastructure.Autodesk → Infrastructure.Sql` | Fixed | **Fixed** | Unchanged this round |
| 19 | UI layer knows Infrastructure/EF | Partial | **Partial** | `SiNet.App.Wpf` still references `Infrastructure.Secrets`; blocked by TFM (`net10.0-windows` vs `net10.0`). Constrained by `WpfSecretsBoundaryTests` — see §4 |
| 20 | Two composition roots; V2 builds its own graph | Partial | **Fixed** | V2 now calls `AddSiNet(SiNetHostMode.V2Hybrid, ...)`; `V2CompositionGraphTests` guards against duplicate registrations |
| 21 | No schema gate on the New System path | Fixed | **Fixed** | Unchanged this round |
| 22 | Legacy dialogs on the New System path | Open | **Partial** | Password prompt is now the native window. The vault/DB `SecretSetupWindow` still runs legacy because it is needed *before* the DI container exists — documented in `APP_SHELL.md` |
| 23 | Sync-over-async in startup/shell | Open | **Partial** | `NewShellFactory.RunSync` removed entirely; New System startup is `async`. Three justified blocking bridges remain on the **Legacy** path — see §4 |
| 24 | Oversized ViewModels / code-behind | Partial | **Open** | Untouched; P2 backlog |
| 25 | Thin Domain, 133 legacy `SiNetSQL.*` usages | Open | **Open** | Untouched; P2 backlog |
| 26 | Stale readiness doc, smoke not current | Open | **Partial** | Test numbers replaced with a measured solution-wide run. **Interactive smoke remains Not Run** |

Round 2 result: **16 Fixed, 4 Partial, 4 Manual Pending, 2 Open** (findings already fixed before
this round and re-confirmed here are counted as Fixed). The audit's own tally was
10 fixed / 6 partial / 10 open, so six findings moved to Fixed and four moved from Open to
Manual Pending — meaning the tooling exists but an operator still has to run it.

## 2. The five new findings from the verification audit

| Ref | New finding | Status | Evidence |
| --- | --- | --- | --- |
| 4.1 | CI cannot gate a clean checkout; "self-contained" claim is false | **Fixed** | `AGENTS.md` and `BUILD_SIBLING_PINS.md` corrected; pins + fetch script added; simulation below |
| 4.2 | TLS pinning not wired into `AddSiNetAutodesk()` | **Fixed** | `AccServiceControlPlaneConfiguration.Bind` used by both roots; `ACC_CONTROL_PLANE.md` rewritten to the real policy |
| 4.3 | API-key fingerprints still written to logs | **Fixed** | `keyLength`/`keyHashPrefix` removed from all five sites; only `hasKey` + `keySource` remain |
| 4.4 | Remediation documents contain inaccurate claims | **Fixed** | `02-script.sql` re-described as a `Db_Mp_SiEng` dump (305 tables); table count corrected to 85 `ToTable` vs 89 `DbSet<>`; board de-escalated from "S0–S7 landed" |
| 4.5 | Decoupling moved the problem: `Infrastructure.Sql → Autodesk connector` | **Fixed** | Port `IAccProjectRootFolderIdReader` in `Application`, implemented in `Infrastructure.Autodesk`; `ProjectReference` deleted from `SiNet.Infrastructure.Sql.csproj`; boundary test asserts it stays deleted |

### Clean-checkout build proof

The audit's central complaint was that no one had shown a fresh checkout building. This is what was
run locally on 2026-07-28:

1. `git clone` of this repository into `D:\repos2026\_ci-sim\`, then an overlay of exactly the files
   `git` reports as modified or untracked-but-not-ignored. Gitignored files were deliberately **not**
   copied, so a file missing from version control would surface as a build failure.
2. `build/fetch-siblings.ps1` — cloned all three siblings **anonymously from GitHub** to their pinned
   SHAs into `_ci-sim\SiNetSQL` and `_ci-sim\AutodeskIntegration\`.
3. `dotnet restore SiNet.sln` — success.
4. `dotnet build SiNet.sln --configuration Release --no-restore` — **0 errors**.
5. `dotnet test SiNet.sln --configuration Release --no-build` — identical results to the working
   tree (2566 passed, 7 failed), confirming nothing depends on untracked local state.

The temporary tree was deleted afterwards.

**What this does not prove:** that the GitHub Actions run itself is green. See §3.

## 3. Known-red: CI will fail on the test step

Seven `SiNet.App.Wpf.Tests` tests fail at HEAD and still fail after this round. They are unrelated
to remediation (stale menu-shape and source-literal assertions, plus two `SqlUserGroup*` services
that legitimately still use `SiNetSQL.Data`) and are listed individually in
[`NEW_SYSTEM_PRODUCTION_READINESS.md §9.2.1`](./NEW_SYSTEM_PRODUCTION_READINESS.md).

Because `ci.yml` runs the test projects, **the workflow will report failure until those seven are
fixed**. The build graph is now correct; the suite is not yet green. Do not read "CI exists" as
"CI passes".

## 4. Deliberate remainders

| Item | Why it was not closed |
| --- | --- |
| `SiNet.App.Wpf` → `Infrastructure.Secrets` | The reference cannot move to `SiNet.App.Composition`: `Infrastructure.Secrets` targets `net10.0-windows`, `App.Composition` targets `net10.0` (`NU1201`). Contained by a boundary test limiting the reference to the composition root |
| Legacy `SecretSetupWindow` at startup | It runs before the DI container is built, so the native window cannot be resolved yet. Requires reordering bootstrap — a separate change |
| Three `GetAwaiter().GetResult()` on the Legacy path | `App.xaml.cs:730/733` sit inside an `IServiceProvider` factory delegate, which is synchronous by contract; `App.xaml.cs:1229` is the Legacy startup pipeline, which is synchronous end to end. All three detour through `Task.Run` to stay off the UI context |
| `packages.lock.json` | Central Package Management with exact versions plus a pinned single feed already makes restore deterministic. Lock files would add per-project churn on every dependency edit for no additional guarantee |
| Git history rewrite for the leaked key | Explicitly declined by the repository owner. Rotation remains the required mitigation |

## 5. Not verified

| Claim | Why it is unverified |
| --- | --- |
| GitHub Actions run is green | The workflow was not dispatched from this session. Only a local equivalent was executed, and the test step is known-red (§3) |
| Database backup, restore rehearsal, Replica migration | Scripts written, **never executed**. They need a live SQL Server and an operator |
| `AUTO_CLOSE` / RCSI actual values | Reporting script written, never executed |
| MasterPlan key rotated/revoked | Requires Autodesk portal access |
| Interactive New System smoke | Requires an operator with DB, vault, Gmail and ACC access |
| Private-repo sibling fetch via `SIBLING_REPOS_TOKEN` | Only the anonymous path was exercised; the repositories are currently public |
