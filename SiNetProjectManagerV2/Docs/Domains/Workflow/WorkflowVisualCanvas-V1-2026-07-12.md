# Workflow Visual Canvas V1 (New System) — 2026-07-12

## Goal
Replace the New Shell **tree** viewer entry with a **read-only visual canvas** (stages + transitions + on-canvas legend). Closed catalogs remain the rule; free text only for names later when editing lands.

## V1 scope
- Native window under `SiNet.App.Wpf/Surfaces/Workflow`
- Load graphs via `IWorkflowClosedViewerQueryService` (includes `CanvasX`/`CanvasY`)
- Definition picker + canvas nodes colored by `NodeType`
- Legend: Start / Stage / Decision / SubWorkflow / End / arrow
- No save, no free-text property editing

## Later (V2)
- Catalog-bound editing (triggers/actions/sub-workflow pickers)
- Wire Start triggers to runtime
- Child open-cap already enforced in runtime (`Workflow.MaxOpenChildInstances`)
