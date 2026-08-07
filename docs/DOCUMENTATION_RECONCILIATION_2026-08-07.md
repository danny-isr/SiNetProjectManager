# Documentation reconciliation -- 2026-08-07

> **Title:** Documentation reconciliation (As-Is alignment)
> **Date:** 07.08.2026
> **Updated:** 07.08.2026 (post-push follow-up -- tip advanced to `127dc0e` / App.Wpf **1.0.23**)
> **Status:** Active (follow-up corrections) -- original measurement block below is a **Historical Snapshot**
> **Classification:** Active ledger for contradiction tracking; §1 original baseline = Historical Snapshot
> **Scope:** Align repository documentation with code, scripts, branches, and publish process. The first pass was documentation-first; a later mixed git commit also included pre-existing ProjectSelector WIP (see §1.2).

---

## 1. Baselines

### 1.0 Current tip (verified post-push follow-up)

| Item | Value |
| --- | --- |
| Verified | **2026-08-07** (follow-up after operator review) |
| Remote tip (`origin/release`) | `127dc0e101bf36d071622ea8761ab24b5084d342` -- `chore(release): ship SiNet.App.Wpf 1.0.23 after ProjectSelector width persistence` |
| Remote tip (`origin/development`) | **Identical** to `origin/release` (`127dc0e`) -- left-right count `0 0` |
| `origin/SiWorkNet10` | `2dfef9e` -- **41 commits behind** `origin/release` |
| GitHub default branch | Still **`SiWorkNet10`** (not changed) |
| Product version on tip | `SiNet.App.Wpf` `<Version>` **1.0.23** |

### 1.1 Original reconciliation baseline (Historical Snapshot)

Measured at the start of the docs-only pass (same calendar day). **Do not** treat as current tip.

| Item | Value |
| --- | --- |
| Remote tip then | `3bfe152` -- ship App.Wpf **1.0.22** |
| `SiWorkNet10` then | **38 commits** behind `release` |
| Product version then | **1.0.22** |

### 1.2 Git commit note (mixed commit)

Wording for the push that landed the reconciliation:

- During the documentation review itself, **no additional product code was authored as part of the review**.
- Commit `13e12ac` (`fix(projects): persist ProjectSelector widths and reconcile As-Is docs`) was a **mixed commit**: documentation changes **plus** pre-existing WIP (`ProjectSelector*`, `EmailWindowViewModel`, tests).
- Accurate statement: **לא נכתב קוד נוסף במסגרת סקירת התיעוד, אך ה־commit כלל WIP קיים.**
- Later ship `127dc0e` bumped App.Wpf to **1.0.23** after that work.

**Dimension glossary (mandatory):**

| Dimension | Examples | Meaning |
| --- | --- | --- |
| Git branch | `development`, `release`, `SiWorkNet10` | Code flow |
| Build configuration | `Debug`, `Release` | Compile / `#if DEBUG` |
| Runtime environment | Development, Production | Vault, DB, ACC, Gmail, Drive |
| Rollout stage | Local -- Candidate -- Pilot -- Production -- Wide | Ops approval |
| Product version | e.g. `1.0.23` (current tip) | Component `<Version>` |

---

## 2. Source-of-truth map (by domain)

| Domain | Active SoT | Notes |
| --- | --- | --- |
| Agent entry | `AGENTS.md` + `.agents/AGENTS.md` | Docs-only rules in `.agents` |
| Environments / dimensions | `docs/ENVIRONMENTS.md` | §0 separates dimensions |
| Release / publish gates | `docs/RELEASE_PROCESS.md` | Manual PROD publish |
| Deploy channels / UNC | Root `DEPLOYMENT.md` + component scripts | Channel 3 = App.Wpf |
| Secrets install order | `SECRETS-MANAGEMENT.md` | Secrets path under V2 folder = Needs Review |
| Desktop host / shell | `docs/APP_SHELL.md`, `docs/STANDALONE_NEW_SYSTEM_HOST.md`, `docs/DESKTOP_CUTOVER.md` | Production = App.Wpf |
| Pilot envelope | `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` | §8.1 = Historical Snapshot |
| Rollout checklist | `docs/ROLLOUT_SINET_APP_WPF.md` | Operational; unchecked -- not Done |
| DEV engineering backlog | `docs/DEV_BACKLOG.md` | Merge -- install |
| Projects / selector | `docs/PROJECTS.md` | Selector not in shell header |
| Workflow ops closed-world | `docs/WORKFLOW_OPS_DASHBOARD.md` | Six codes As-Is |
| Workflow coexistence (old vs new tasking) | V2 `TaskingWorkflowCoexistence-*` | Catalog count fixed; business redesign postponed |
| Architecture target | `docs/ARCHITECTURE_TARGET.md` | Planning / Target State |
| Migration ledger | `docs/MIGRATION_MAP.md` | Historical Snapshot |
| Docs index | `docs/README.md` | Must list this file |
| V2 domain index | `SiNetProjectManagerV2/Docs/README.md` | Domain principles |

---

## 3. Contradiction ledger

| ID | File | Old claim | Evidence | Class | Fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
| R-001 | `docs/APP_SHELL.md` | Working branch `SiWorkNet10`; Legacy = current production; dual-mode as primary; ProjectSelector in shell header | `NewShellWindow.xaml` has no selector; App.Wpf standalone startup; `publish-all` channel 3 | Wrong / Stale | Rewrote §§1-5,9,12-16; header | Done |
| R-002 | `docs/RELEASE_PROCESS.md` | "once those branches exist"; version not in shell title; Out of Scope "implement version display" | Branches since 03.08.2026; `NewShellWindowTitle.cs` + tests | Stale / Wrong | Wording + version As-Is + dimensions | Done |
| R-003 | `docs/PRODUCTION_MONITORING.md` | "No version in shell UI" gap | Same as R-002 | Stale | Gap row updated | Done |
| R-004 | `docs/DEV_BACKLOG.md` | Many "awaiting PROD publish" while tips identical + ship commits | `origin/release`==`development` @ `3bfe152`; ship commits through 1.0.22 | Stale / Ambiguous | §2b ops-verify Needs Review; empty Done until verify | Done |
| R-005 | `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` | §8.1 readable as current; selector in host row | Snapshot on `SiWorkNet10` 2026-08-02; XAML | Historical / Wrong | Historical framing + evidence layers | Done |
| R-006 | `docs/ROLLOUT_SINET_APP_WPF.md` | Missing metadata; risk of inferring Done | Checkboxes unchecked; empty sign-off | Ambiguous | Explicit repo-vs-ops table | Done |
| R-007 | `docs/ENVIRONMENTS.md` | Soft Debug=DEV / MSIX=PROD coupling | Operator can Release-build on DEV | Ambiguous | §0 dimensions + matrix columns | Done |
| R-008 | Root `DEPLOYMENT.md` | Channel table lists V2 as desktop; DesktopV2 steps | `publish-all.ps1` channel 3 = App.Wpf; SyncEngine `DeployDir`=`MasterPlan.SyncEngine` | Wrong | Rewrote channel table + install path | Done |
| R-009 | `SECRETS-MANAGEMENT.md` | V2 as WPF on user PCs; MSIX to V2 share | Cutover | Wrong / Ambiguous | Host = App.Wpf; secrets path Needs Review | Done |
| R-010 | `docs/SETTINGS.md` | "production runs under SiNetProjectManagerV2" | Cutover / App.xaml.cs | Wrong | Production host wording | Done |
| R-011 | `docs/PROJECTS.md` | Working branch SiWorkNet10; live shell = V2 MainWindow | `NewShellWindow` + `NewShellWindowTitle` | Wrong / Stale | Header + §11 status | Done |
| R-012 | `docs/ARCHITECTURE_TARGET.md` | Working branch SiWorkNet10; "pre-production / no live users" | Pilot host shipped | Wrong / Stale | Existing/Target/Historical split | Done |
| R-013 | Headers in ACC/GOOGLE/FILE_CATALOG/MIGRATION/PROJECT_CONTEXT | Working/`Branch: SiWorkNet10` | RELEASE_PROCESS §3.2 | Wrong | Headers retargeted | Done |
| R-014 | V2 `DeploymentPrinciples` + `SiNetProjectManagerV2/DEPLOYMENT.md` | Active SoT naming V2 MSIX | `publish-all` no longer calls V2 | Stale Active SoT | Marked Superseded | Done |
| R-015 | V2 workflow docs ("all 5 workflows") | Missing Outsourcing | `WorkflowCodes` + seed order (6) | Stale | Corrected to six; business redesign Needs Review | Done |
| R-016 | `docs/GOOGLE_BOUNDARY.md` | "Production today: V2 still uses legacy Google" | App.Wpf native Google module is production desktop path | Ambiguous / Wrong | Split desktop host vs V2 reference | Done |
| R-017 | `publish-all.ps1` header comment `MasterPlanSync\` | vs `DeployDir` | Comment stale; DeployDir = `MasterPlan.SyncEngine` | Stale (script comment) | Documented in RELEASE/DEPLOYMENT -- **script not edited** (docs-only) | Documented |
| R-018 | GitHub default = `SiWorkNet10` | Working SoT is release/development | `git remote show` | Ambiguous (ops) | Documented; change = separate recommendation | Needs Review (ops) |

---

## 4. Documents marked Superseded / Historical

| Document | New classification | Replacement / pointer |
| --- | --- | --- |
| `SiNetProjectManagerV2/Docs/Domains/Deployment/DeploymentPrinciples-2026-05-26.md` | Superseded (desktop SoT) | `docs/RELEASE_PROCESS.md`, root `DEPLOYMENT.md` |
| `SiNetProjectManagerV2/DEPLOYMENT.md` | Superseded / Historical | `src/SiNet.App.Wpf/publish-desktop.ps1` + RELEASE_PROCESS |
| `docs/MIGRATION_MAP.md` | Historical Snapshot / ledger | Keep body; header clarified |
| `docs/PROJECT_CONTEXT_MIGRATION.md` | Historical Snapshot | `docs/PROJECTS.md` wins |
| `docs/NEW_SYSTEM_PRODUCTION_READINESS.md` §8.1 | Historical Snapshot | Re-run tests on tip |
| Audit docs under `docs/AUDIT-*` | Historical Snapshot | Unchanged body |

**Not moved to Archive** (requires explicit approval). **Not deleted.**

---

## 5. Needs Review (remaining)

1. **Pilot/ops:** Whether UNC share and pilot PCs actually run App.Wpf **1.0.23** (ship commits -- install proof).
2. **`SiNet.secrets` path** on the share -- still under `SiNetProjectManagerV2\` or moved.
3. **GitHub default branch** change away from `SiWorkNet10` (separate recommendation).
4. **DEV-008/009/010/011** completeness vs tip (Implementing rows).
5. **Workflow business** stage-by-step redesign -- postponed; do not invent business rules from seed alone.
6. **ServiceCatalog** legacy SiNetSQL seed class path -- confirm New System seed ownership narrative in a later docs pass.
7. `MoveToProject-Decisions-2026-05-24.md` -- linked from V2 Docs README but **file not in repo**. **Interim/canonical FileMaterial Target:** [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) (2026-08-07). Restore historical decisions file from git/Archive if still needed for Legacy audit.

### 5.1 Follow-up fixes (operator review 2026-08-07)

| ID | Issue | Fix |
| --- | --- | --- |
| R-019 | Ledger still claimed Current SoT at `3bfe152` / 1.0.22 after tip advanced | §1.0 current tip `127dc0e` / **1.0.23**; original baseline Historical |
| R-020 | Mixed commit wording | §1.2 -- docs review did not author new product code; commit included existing WIP |
| R-021 | `GOOGLE_BOUNDARY` §2.1 called V2 "production host" | Renamed to V2 reference / hybrid host |
| R-022 | ServiceCatalog desktop publish script | Point to `src/SiNet.App.Wpf/publish-desktop.ps1` |
| R-023 | `DEPLOYMENT.md` SecretImport path `src\SiNet.SecretImport\...` | Correct to `SiNet.SecretImport\publish-tool.ps1` |
| R-024 | `SECRETS-MANAGEMENT` said publish from DEV machine | PROD workstation only |
| R-025 | Broken links (MoveToProject-Decisions, Email-ManualQA runbook, chat GUID) | Marked / repaired |
| R-026 | Missing Date/Status/Scope on updated docs | Metadata filled on deficient headers |

---

## 6. Documentation gap vs product gap

| Kind | Examples |
| --- | --- |
| **Documentation gap** (fixed this round) | Version-in-title denial; V2 as publish channel; SiWorkNet10 as working branch; five workflows; Legacy as production |
| **Product / ops gap** (still open) | Rollout phases unchecked; interactive smoke Not Run; OPS-P0 backup/rotation; G-Policy send; `SINET_ENVIRONMENT` Not Implemented |
| **Neither** | Changing default branch (ops/GitHub), workflow business redesign (postponed by request) |

---

## 7. Out of Scope

- Any code, XAML, ViewModel, DI, DB, migration, seed, or publish-script edits
- Publishing, installing, commit, push, PR
- Changing GitHub default branch
- Workflow step-by-step business realignment (postponed to next round)
- Moving docs into `Docs/Archive` without approval
- Full automated test re-run on tip (docs-only; explicitly not run)

---

## 8. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Workflow business migration step-by-step | Postponed | Wait until docs As-Is alignment finishes |
| Deleting or archiving superseded docs | Cancelled for this round | Mark + link only |
| Editing `publish-all.ps1` stale `MasterPlanSync\` comment | Postponed | Docs-only; tracked as R-017 |
| Changing GitHub default branch | Postponed | Separate recommendation after review |
| No mechanism removed from product | -- | Docs only |

---

## 9. Cross-references added

- This file -- `docs/README.md`, `AGENTS.md`, `APP_SHELL`, `RELEASE_PROCESS`, `ENVIRONMENTS`, `DEV_BACKLOG`, `ROLLOUT`, `NEW_SYSTEM_PRODUCTION_READINESS`, V2 DeploymentPrinciples, V2 DEPLOYMENT, V2 Docs README, TaskingWorkflowCoexistence.

---

## 10. Scan coverage (this round)

Scanned / spot-checked: `AGENTS.md`, `.agents/AGENTS.md`, `.cursor/rules/*.mdc`, root `*.md`, `docs/**/*.md` (incl. manual-tests), active V2 Domains (Deployment, Workflow, Architecture ServiceCatalog), V2 Decisions not rewritten as live SoT, component DEPLOYMENT notes. Archive not used as SoT and not rewritten as "today."

Approximate markdown inventory in repo: **~170** `.md` files; high-severity contradictions above were corrected; remaining historical mentions of `SiWorkNet10` / old test counts must stay explicitly Historical.
