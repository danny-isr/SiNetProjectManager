# .NET Version Upgrade — SiNetProjectManager

## Strategy
**Selected**: All-At-Once
**Rationale**: 10 projects on uniform `net8.0` baseline, clear dependency structure (4 levels), straightforward .NET 8 → .NET 10 upgrade, all packages have clear compatible target versions, no major breaking API changes expected.

### Execution Constraints
- Single atomic upgrade — TFMs and packages updated across the full solution before any code fixes
- Build the full solution after the atomic update; fix all compilation errors in a single bounded pass (no fix-build-fix loop)
- Tests run only after the build is clean across all 10 projects
- Solution spans 4 separate git repositories — each repo gets its own branch (`upgrade-to-NET10`) and its own final commit/PR
- Repo boundary: `SiNetProjectManager_GitHub`, `SiNetSQL`, `SiOffice.AutodeskConnector`, `SiOffice.GoogleConnector`

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: Single Commit at End (per repo) — one consolidated commit per repository at the end of the upgrade
- **Pace**: Standard
- **Target Framework**: `net10.0` / `net10.0-windows` (preserves platform suffix per project)
- **Source Branch**: `SiWork` (in all 4 repos)
- **Working Branch**: `upgrade-to-NET10` (in all 4 repos)
- **Language**: User communicates in Hebrew; respond in Hebrew

## Decisions
- Treat `Microsoft.Xaml.Behaviors.Wpf` "incompatible" flag as needs-verification — current `1.1.135` may already support net10; only downgrade/replace if confirmed needed
- `Api.0001` (binary-incompatible) issues are not actionable until they manifest as actual build errors — assessment flags 2,704 of these but most resolve automatically on recompile
- Test projects (`SiNetSQL.Tests`, `SiNetSQL.E2ETests`) currently use `xunit 2.6.6` (deprecated) — bump to latest stable xunit 2.x; defer migration to xunit.v3

## Custom Instructions
<!-- Task-specific overrides: "For {taskId}: {instruction}" -->
