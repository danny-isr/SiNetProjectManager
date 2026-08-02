# Desktop cutover — SiNet.App.Wpf replaces V2

> **Status:** Approved (2026-08-02)  
> **Related:** [`APP_SHELL.md`](./APP_SHELL.md), [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),  
> [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md), [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md)

## Decision

| Item | Value |
| --- | --- |
| Shipped desktop app | `SiNet.App.Wpf` (MSIX + `.appinstaller`) |
| Share folder | `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf` |
| Package identity | `SiNet.App.Wpf` (distinct from historical `SiNet.ProjectManagerV2`) |
| `SiNetProjectManagerV2` | Remains in repo / hybrid solution; **not** published |
| Office safety net until cutover | Existing external legacy system (outside this repo) |

## Publish channel

`publish-all.ps1` channel **3/4** invokes `src/SiNet.App.Wpf/publish-desktop.ps1`.

```powershell
pwsh .\publish-all.ps1 -SkipService -SkipConsole -SkipTool
# or directly:
pwsh .\src\SiNet.App.Wpf\publish-desktop.ps1
```

## Rollout outline

1. Ops P0: MasterPlan key rotation + DB backup/restore drill.
2. Publish signed MSIX to the new share.
3. Smoke on a clean machine from `.appinstaller`.
4. 1–2 pilot users while the external legacy system stays available.
5. Expand after sign-off; do not uninstall the external system until then.
