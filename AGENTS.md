# SiNet Project Manager -- Agent Instructions

> **Status:** Active
> **Updated:** 07.08.2026
> **Scope:** Repo-wide entry for Cursor / AI agents (environments, build gate, key docs). Domain behavior lives in `docs/`.

This file is the **entry point for Cursor / AI agents** working in this repository.

## Environment & machine roles

Two workstations use this repo. They are **not** interchangeable. Read [`docs/ENVIRONMENTS.md`](docs/ENVIRONMENTS.md) before DevTools, publish, or ACC/Drive writes.

| Role | Machine | Agents may | Agents must not |
| --- | --- | --- | --- |
| **PROD** | Release + ops workstation | Small fixes; help with release gates; ops/log guidance | Run DevTools Reset/Seed against production SQL; treat Google/ACC as a sandbox |
| **DEV** | Development workstation | Feature work, Debug, DevTools against **dev DB only** | Run `publish-all.ps1` to the production UNC share |

- **Release protocol:** [`docs/RELEASE_PROCESS.md`](docs/RELEASE_PROCESS.md) -- only PROD publishes to `\\SI-WIN-2K19\AppFolder\AppNet\`. Branches: `release` (checked out on PROD, ships) and `development` (checked out on DEV, must absorb `release` after every ship). `SiWorkNet10` is deprecated but retained -- see `docs/RELEASE_PROCESS.md` §3.2. GitHub **default branch** may still be `SiWorkNet10` (ops setting; Needs Review).
- **Pilot monitoring:** [`docs/PRODUCTION_MONITORING.md`](docs/PRODUCTION_MONITORING.md).
- **ACC on DEV:** only projects with place name **`SI`** -- see [`docs/ENVIRONMENTS.md`](docs/ENVIRONMENTS.md) §5.1.
- **Docs index:** [`docs/README.md`](docs/README.md).
- **Documentation As-Is reconciliation (2026-08-07):** [`docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md`](docs/DOCUMENTATION_RECONCILIATION_2026-08-07.md) -- dimension glossary (branch vs build vs runtime vs rollout vs version); contradiction ledger.

**Dimension note:** Do not conflate git branch, MSBuild `Debug`/`Release`, runtime Development/Production, rollout stage, and product `<Version>`. See `docs/ENVIRONMENTS.md` §0 and the reconciliation ledger.

If it is unclear which machine/DB the session is on, **ask the operator** before destructive or publish actions.

## Where instructions live (priority order)

| Layer | Path | Use for |
| --- | --- | --- |
| **1. Cursor User Rules** | Cursor Settings → Rules | Personal preferences across all repos |
| **2. Project rules (always on)** | `.cursor/rules/*.mdc` | Build gate, coding standards, slice guardrails |
| **3. This file** | `AGENTS.md` | Repo-wide agent entry + links |
| **4. Extended agent docs** | `.agents/AGENTS.md` | Documentation-round rules (docs-first, metadata) |
| **5. Domain docs** | `docs/*.md` | Architecture, migration slices, feature behavior |
| **6. Chat message** | One-off task prompt | Scope for a single slice (acceptance criteria, files) |

**Rule of thumb:** put **repeatable** constraints in `.cursor/rules/`. Put **domain behavior** in `docs/`. Put **one slice** details in the chat (or a migration doc section).

## Build contract (CI vs local)

| Solution | Role | Notes |
| --- | --- | --- |
| **`SiNet.sln`** | **Official CI solution** | GitHub Actions (`.github/workflows/ci.yml`) fetches the pinned sibling repositories, restores, builds Debug + Release, runs all three test projects under `src/`, and secret-scans every tracked text file. |
| **`SiNetProjectManager.sln`** | Hybrid legacy + new stack | Same sibling pins, plus the legacy projects. **Not** the CI gate. |

**Neither solution is self-contained.** Both reach `SiNetSQL`, `SiOffice.AutodeskConnector` and
`SiOffice.GoogleConnector`, which are checked out next to this repository at commits pinned in
[build/sibling-pins.json](build/sibling-pins.json). On a clean machine run
`pwsh .\build\fetch-siblings.ps1` before building. See `docs/BUILD_SIBLING_PINS.md`.

Local agent gate (production host + primary test project):

```powershell
cd SiNetProjectManager_GitHub
dotnet build src\SiNet.App.Wpf\SiNet.App.Wpf.csproj
dotnet test src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj
```

Full CI-equivalent check locally:

```powershell
dotnet build SiNet.sln --configuration Release
dotnet test SiNet.sln --configuration Release --no-build
```

**Do not** treat historical **955/955** test counts (2026-07-05 snapshot in `docs/NEW_SYSTEM_PRODUCTION_READINESS.md`) as evidence for current HEAD — run tests on the branch you are changing.

Report in the final message: build result, test result, and whether DB/schema changed.

## Key docs

- `docs/README.md` — Index of all `docs/*.md`
- `docs/ENVIRONMENTS.md` — PROD vs DEV machine roles and config placement
- `docs/RELEASE_PROCESS.md` — Publish gates, versioning, rollback
- `docs/PRODUCTION_MONITORING.md` — Live logs and health during pilot
- `docs/OPS_ACCSERVICE_TOKEN_REFRESH.md` — Restore AccService Autodesk 3-legged refresh token
- `docs/OPS_STARTUP_ALERTS.md` — Planning: admin startup alerts (DEV; not PROD-only ops)
- `docs/DEV_BACKLOG.md` — Open DEV defects index (start here on development machine)
- `docs/DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md` — DEV-001 Jumbo/body link → external window → ACC
- `docs/DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md` — DEV-003 ProjectWork bak/recover/tree UX wishlist
- `docs/APP_SHELL.md` — Production shell is `SiNet.App.Wpf` (V2 not shipped)
- `docs/STANDALONE_NEW_SYSTEM_HOST.md` — Standalone host composition and cutover
- `docs/WORKFLOW_OPS_DASHBOARD.md` — Workflow runtime ops (closed-world definitions)
- `docs/PROJECTS.md` — Shared ProjectSelector, project context
- `docs/PROJECTS_DASHBOARD.md` — Projects overview dashboard («ריכוז פרויקטים»)
- `docs/PROJECT_CONTEXT_MIGRATION.md` — Migration slice notes
- `docs/ARCHITECTURE_TARGET.md` — Target architecture
- `docs/EMAIL_ACC_SOURCE_OF_TRUTH.md` — **Email/ACC sources of truth** (Gmail label = mailbox filed; ACC = physical file; DB = helper)
- `docs/FILEMATERIAL_MOVETOPROJECT.md` — **FileMaterial / MoveToProject** six decisions (canonical Target)
- `SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md` — Email domain principles (§6.6 mailbox association)
- `SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md` — ACC domain principles

## New stack layout

- `src/SiNet.App.Wpf/` — **Production desktop host** (WPF surfaces + shell)
- `src/SiNet.Application/` — Ports, DTOs, query logic
- `SiNetProjectManagerV2/` — Legacy host kept as code reference / hybrid build; **not** the publish channel

See `.agents/AGENTS.md` for documentation-only round rules.
