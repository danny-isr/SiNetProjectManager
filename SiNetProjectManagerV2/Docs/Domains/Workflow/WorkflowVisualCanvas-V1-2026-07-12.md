# Workflow Visual Canvas V1 (New System) — 2026-07-12

## Goal
Native visual canvas in New Shell for workflow definitions (stages + transitions + legend + inspect).

## V1 scope
- Native window under `SiNet.App.Wpf/Surfaces/Workflow`
- Load graphs via `IWorkflowClosedViewerQueryService` (includes `CanvasX`/`CanvasY`)
- Definition picker + nodes colored by `IsInitial`/`IsFinal`/`NodeType`
- **Drag nodes** (layout only) + **שמור פריסה** → `IWorkflowCanvasLayoutService` writes CanvasX/Y only
- **Click stage / edge** → detail panel: required tasks, trigger/condition/result, actions, outgoing transitions
- Reverse pairs (A→B and B→A) drawn as two offset one-way arrows; self-loops drawn above the node
- Seed fills Canvas only when existing coords are blank (manual layout survives re-seed)
- No stage/transition content editing in this surface

## Bidirectional arrows
There is **no** bidirectional transition type. What looks two-way is two separate `WorkflowTransitionRule` rows.

## Self-loops (e.g. Proposal `PRP.MaterialCheck`)
A transition with `FromStageId == ToStageId` means **stay in the same stage** when a condition matches (e.g. `QuoteMaterialMissing`). Exit via a different outgoing rule (e.g. `QuoteMaterialComplete` → Calculation). Trigger may be `Manual` (seed default) — not auto-advance.

## Later
- Catalog-bound content editing
- Promote arranged Canvas coords into seed sources once layout is stable
- Wire Start triggers to runtime
- Closed Viewer tree: include self-loops under a “לולאה” group
