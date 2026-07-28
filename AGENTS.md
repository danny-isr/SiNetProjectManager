# SiNet Project Manager — Agent Instructions

This file is the **entry point for Cursor / AI agents** working in this repository.

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

Local agent gate (host app + primary test project):

```powershell
cd SiNetProjectManager_GitHub
dotnet build SiNetProjectManagerV2\SiNetProjectManagerV2.csproj
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

- `docs/APP_SHELL.md` — Legacy vs New system shell; `StartupModeSelectionWindow` is first UI
- `docs/PROJECTS.md` — Shared ProjectSelector, project context
- `docs/PROJECT_CONTEXT_MIGRATION.md` — Migration slice notes
- `docs/ARCHITECTURE_TARGET.md` — Target architecture
- `docs/EMAIL_ACC_SOURCE_OF_TRUTH.md` — **Email/ACC sources of truth** (Gmail label = mailbox filed; ACC = physical file; DB = helper)
- `SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md` — Email domain principles (§6.6 mailbox association)
- `SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md` — ACC domain principles

## New stack layout

- `src/SiNet.App.Wpf/` — WPF surfaces + shared controls (e.g. `Shared/Projects/ProjectSelectorView`)
- `src/SiNet.Application/` — Ports, DTOs, query logic
- `SiNetProjectManagerV2/` — Legacy host (composition root today)

See `.agents/AGENTS.md` for documentation-only round rules.
