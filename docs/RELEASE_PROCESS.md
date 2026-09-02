# Release Process — SiNet.App.Wpf and companion channels

> **Title:** Release Process
> **Date:** 02.08.2026
> **Updated:** 07.08.2026 (As-Is reconciliation -- branches exist; version in OS title; dimensions; SyncEngine folder; default branch note)
> **Status:** Active / Current Source of Truth
> **Scope:** How the PROD workstation ships builds to users; what changes are allowed where; gates before `publish-all.ps1`; versioning and rollback. Does not replace channel-level install detail in [`DEPLOYMENT.md`](../DEPLOYMENT.md).
> **Reconciliation:** [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md)

Related: [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md), [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md), [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md), [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md), [`BUILD_SIBLING_PINS.md`](./BUILD_SIBLING_PINS.md).

---

## 0. Dimension separation (do not conflate)

| Dimension | Examples | Meaning |
| --- | --- | --- |
| Git branch | `development`, `release`, `SiWorkNet10` | Code flow. Working SoT = `release` + `development`. |
| Build configuration | `Debug`, `Release` | Compile / `#if DEBUG` |
| Runtime environment | Development, Production | Vault, DB, ACC, Gmail, Drive |
| Rollout stage | Local -- Candidate -- Pilot -- Production -- Wide | Ops approval |
| Product version | e.g. `1.0.23` | Component `<Version>` (tip verified 2026-08-07 follow-up) |

**GitHub default branch** is still **`SiWorkNet10`** (ops/GitHub setting) -- deprecated as working SoT; change is **Needs Review** (not done in this docs round).

---

## 1. Purpose

Define a single release protocol so both machines agree:

- **PROD** (this workstation) is the **only** station that publishes to `\\SI-WIN-2K19\AppFolder\AppNet\`.
- **DEV** builds and tests; it does not overwrite the production share.
- Near-term work on PROD is limited to **small fixes and small feature updates** while the pilot runs; larger work stays on DEV until it is ready to merge and ship.

There is **no** GitHub Actions release/publish workflow today. Shipping is **manual** from PROD.
**Automated validation** for development / certification / release-candidate readiness is operator/agent-run **locally on the DEV workstation** (Cursor). `.github/workflows/ci.yml` is dormant historical configuration and is **not** an active release gate.

---

## 2. Change policy (what belongs where)

| Change type | Preferred machine | Ship from |
| --- | --- | --- |
| Tiny bugfix, copy/UI polish, ops-critical hotfix | PROD or DEV | PROD after gates |
| Small feature update (localized, low risk) | Either; prefer DEV if ACC/Drive involved | PROD after gates |
| Larger feature, schema-touching work, ACC/Drive/Gmail filing changes | **DEV only** | PROD after merge + gates |
| DevTools / seed / experimental spikes | **DEV only** | Never against PROD DB |
| Publish scripts / MSI / MSIX packaging changes | Prefer DEV, verify once on PROD | PROD |

**PROD operating mode during pilot:** keep the working tree close to what users run. Avoid long-lived half-finished features on the release branch.

---

## 3. Branch policy (decided 02.08.2026)

**Decision:** two long-lived branches — **`release`** (production / ship) and **`development`** (day-to-day DEV work). Both were created on 03.08.2026 from `SiWorkNet10` at commit `2dfef9e`, so all three share that history.

| Branch | Machine / role | Purpose |
| --- | --- | --- |
| **`release`** | PROD workstation | What users get. Small fixes and small feature updates that ship via `publish-all.ps1`. Keep close to what is installed. |
| **`development`** | DEV workstation | Larger features, experiments, DevTools. Must regularly **absorb `release`** so production fixes are never lost. |

```text
                    publish-all.ps1
  release  ──────────────────────────►  UNC / users
     │
     │  after every ship / hotfix on release:
     │  merge release → development
     ▼
  development  (continues feature work on top of shipped fixes)
```

### 3.1 Merge rules

1. **Every update that lands on `release` and is shipped (or is about to ship) must be merged into `development`.**  
   Direction: **`release` → `development`**. This is mandatory so a later DEV→release promotion does not reintroduce bugs that were already fixed in production.
2. **Promotion of new work:** when a feature on `development` is ready for users, merge (or PR) **`development` → `release`**, then run the release gate (§5) on PROD from `release`. Prefer merging only when `development` already contains the latest `release` tip.
3. **Hotfixes during pilot:** may be committed on `release` on the PROD machine; still merge into `development` the same day.
4. Short-lived `feat/…` / `fix/…` branches are optional and should fork from / merge into **`development`**, not from an outdated base.

### 3.2 Machine → branch assignment

| Machine | Checked out branch | Publishes |
| --- | --- | --- |
| **PROD** (release + ops workstation) | `release` | Yes — `publish-all.ps1` to the UNC share |
| **DEV** (development workstation) | `development` | No |

On the DEV machine, absorb a shipped fix with:

```powershell
git fetch origin
git checkout development
git merge origin/release
git push origin development
```

**`SiWorkNet10` — deprecated, retained.** It was the single active branch until 03.08.2026 and is the common ancestor of both new branches. Status: **no longer the working branch on either machine**; kept because CI history, older documentation and existing references point at it. It may still be pushed to by an automation or a stale checkout, so do not treat it as authoritative. Do not delete it before confirming nothing (CI runs, automations, the DEV checkout) still targets it.

### 3.3 Automated validation (local DEV — authoritative)

**Current SoT:** run the full `SiNet.sln` gate locally on DEV (siblings → Debug build → Release build → App.Wpf / Google / MasterPlan.SyncEngine / LegacyBridge tests → secret-scan). See [`AGENTS.md`](../AGENTS.md).

> **Documentation drift (historical):** older text stated that GitHub Actions (`.github/workflows/ci.yml`) “must be green on `release` before a publish.” That workflow is **not** an active gate — it was an experimental/historical attempt and may remain disabled. Do not treat GHA run status as product/test failure.

---

## 4. Release station prerequisites (PROD)

One-time / verify before first pilot publish (also listed in [`DEPLOYMENT.md`](../DEPLOYMENT.md)):

1. Visual Studio / MSBuild available (`vswhere`).  
2. .NET SDK matching [`global.json`](../global.json) (pinned `10.0.301`, rollForward `latestFeature`).  
3. Windows 10/11 SDK (`MakeAppx.exe`, `SignTool.exe`).  
4. WiX Toolset (AccService MSI channel).  
5. Code-signing certificate (`CN=SI Office`) in `Cert:\CurrentUser\My`.  
6. Write access to `\\SI-WIN-2K19\AppFolder\AppNet\`.  
7. Sibling repos present at pins: `pwsh .\build\fetch-siblings.ps1` (see [`BUILD_SIBLING_PINS.md`](./BUILD_SIBLING_PINS.md)).

---

## 5. Release gate (every publish)

Run on the **PROD** machine, from the repo root, on the commit you intend to ship.

### 5.1 Sync and siblings

```powershell
cd D:\Repos2026\SiNetProjectManager_GitHub
git status
git pull
pwsh .\build\fetch-siblings.ps1
```

Confirm working tree is intentional (no surprise local experiments).

### 5.2 Build and test (CI-equivalent)

```powershell
dotnet build SiNet.sln --configuration Release
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj --configuration Release --no-build
dotnet test src\SiNet.Infrastructure.Google.Tests\SiNet.Infrastructure.Google.Tests.csproj --configuration Release --no-build
dotnet test src\SiNet.LegacyBridge.Tests\SiNet.LegacyBridge.Tests.csproj --configuration Release --no-build
pwsh .\build\secret-scan.ps1
```

Local agent minimum (if only the desktop host changed):

```powershell
dotnet build src\SiNet.App.Wpf\SiNet.App.Wpf.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
```

Do **not** publish if build, tests, or secret-scan fail.

### 5.3 Ops P0 checklist (before expanding the pilot)

| Gate | Doc | Status expectation |
| --- | --- | --- |
| DB backup + restore drill | [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md) | Must not remain “Manual Pending” before wide rollout |
| MasterPlan API key rotation | [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) | Same |
| Local full `SiNet.sln` gate on DEV | Cursor / local shell (siblings, Debug+Release, four test projects, secret-scan) | Required every ship — **not** GitHub Actions |
| Smoke on a clean / pilot machine | [`manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md`](./manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md) | Required for first install and after risky changes |

### 5.4 Publish

```powershell
# All four channels
powershell -ExecutionPolicy Bypass -File .\publish-all.ps1

# Desktop only
.\publish-all.ps1 -SkipService -SkipConsole -SkipTool

# Build without touching the share (dry packaging)
.\publish-all.ps1 -SkipDeploy
```

Channels (authoritative list in `publish-all.ps1`):

| # | Component | Output | Share folder |
| --- | --- | --- | --- |
| 1 | `SiOffice.AccService` | WiX MSI | `...\SiProjecNet2026-Full\` |
| 2 | `MasterPlan.SyncEngine` | self-contained EXE | `...\MasterPlan.SyncEngine\` (`DeployDir` in `publish-console.ps1` -- not `MasterPlanSync\`; a stale comment in `publish-all.ps1` may still say otherwise) |
| 3 | **`SiNet.App.Wpf`** | MSIX + `.appinstaller` | `...\SiNet.App.Wpf\` |
| 4 | `SiNet.SecretImport` | portable EXE | `...\SiNet.SecretImport\` |

`SiNetProjectManagerV2` is **not** a publish channel — see [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md).

After the four channels, `publish-all.ps1` also assembles the **Server kit** at
`\\SI-WIN-2K19\AppFolder\AppNet\Server\` (MSI + SecretImport + `Install-OnServer.ps1` + README).
On the server (no `D:\repos` needed):

```powershell
# Prefer the CMD wrapper:
\\SI-WIN-2K19\AppFolder\AppNet\Server\Upgrade-AccService.cmd

# Or positional Mode (do not use -SkipImport):
powershell -NoProfile -ExecutionPolicy Bypass -File D:\SharedFolder\AppFolder\AppNet\Server\Install-OnServer.ps1 Upgrade
```

### 5.5 After publish

1. Confirm new MSIX / `.appinstaller` timestamps on the UNC share.  
2. Commit bumped `<Version>` values in the relevant `.csproj` files (publish scripts bump patch by default unless `-NoBump`).  
3. **Merge `release` → `development`** so production fixes are absorbed -- see §3.1. Both branches have existed since 03.08.2026.
4. Optional git tag on the shipped `release` commit (`Needs Review` -- see §11).
5. Run post-release monitoring: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) §4.
6. Pilot users: next app launch picks up MSIX update via `.appinstaller` (`OnLaunch` checks).

First-time install on a user PC:

```text
\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\SiNet.App.Wpf.appinstaller
```

(Exact filename follows `src/SiNet.App.Wpf/publish-desktop.ps1` output.)

---

## 6. Versioning

| Component | Where version lives | Bump behavior |
| --- | --- | --- |
| `SiNet.App.Wpf` | `<Version>` in `src/SiNet.App.Wpf/SiNet.App.Wpf.csproj` | `publish-desktop.ps1` bumps **patch** unless `-NoBump` |
| AccService | Its `.csproj` | `publish-service.ps1` |
| SecretImport | Its `.csproj` | `publish-tool.ps1` |
| SyncEngine | Seeded / bumped by its publish script | Independent |

There is **no** single monorepo version. MSIX package version is four-part; revision must remain `0` for `.appinstaller` updates.

**As-Is (verified):** `SiNet.App.Wpf` **does** show the product version in the **OS window title** via `NewShellWindowTitle` (bound from `NewShellViewModel.WindowTitle`; assembly informational version from csproj `<Version>`). Operators can also correlate UNC MSIX / `.appinstaller` timestamps and optional git tags. There is no separate in-chrome "About" version widget -- that is optional polish, not a missing title.

---

## 7. Rollback

| Layer | Action |
| --- | --- |
| Desktop MSIX | Restore previous `.msix` + `.appinstaller` pair on the share (keep last-known-good copies outside `/MIR` wipe if needed), then have users relaunch or reinstall from `.appinstaller` |
| AccService | Re-run previous MSI / MajorUpgrade from retained artifact; see `SiOffice.AccService/DEPLOYMENT.md` |
| SyncEngine | Robocopy previous EXE folder back onto the share |
| Database | Restore from backup per [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md) — only if the release included schema or data changes |

Prefer **app rollback without DB rollback** when the schema did not change.

---

## 8. What this document does not replace

- Per-channel install and troubleshooting tables: [`DEPLOYMENT.md`](../DEPLOYMENT.md) and each component’s own `DEPLOYMENT.md`.  
- Pilot phase checklist: [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md).  
- Secrets install order: [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md).

> **Note:** Root [`DEPLOYMENT.md`](../DEPLOYMENT.md) still describes some V2 desktop paths historically. Authoritative desktop channel for new installs is **`SiNet.App.Wpf`** (channel 3 in `publish-all.ps1`). Prefer this document + [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md) when they disagree on the desktop host name.

---

## 9. Out of Scope

- Automating publish in GitHub Actions  
- Schema migrations as part of release (operator-owned; agents must not run them)
- Deleting `SiWorkNet10` (see §3.2 -- deprecated but retained until nothing targets it)
- Changing the GitHub default branch away from `SiWorkNet10` (separate ops recommendation)
- Editing the stale `MasterPlanSync\` comment inside `publish-all.ps1` in a docs-only round

## 10. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Publishing `SiNetProjectManagerV2` MSIX | Dropped | Cutover to `SiNet.App.Wpf` |
| ClickOnce as production channel | Dropped / legacy | Replaced by MSIX + `.appinstaller` |
| CI-driven release | Postponed | Manual PROD publish is intentional for the pilot |
| Single-branch forever on `SiWorkNet10` | Dropped as target | Replaced by `release` + `development` (§3) |
| Development merging *into* release on every hotfix without absorbing the other way | Dropped | Wrong direction; production fixes must flow **into** development |

## 11. Needs Review

1. Whether every publish must create a git tag.
2. Retention policy for last-known-good MSIX outside the mirrored share folder.
3. When `SiWorkNet10` can be deleted -- requires confirming that no CI run, automation or DEV checkout still targets it.
4. Whether GitHub **default branch** should switch from `SiWorkNet10` to `release` or `development`.
5. Ops verify that pilot UNC install actually runs App.Wpf **1.0.23**.
