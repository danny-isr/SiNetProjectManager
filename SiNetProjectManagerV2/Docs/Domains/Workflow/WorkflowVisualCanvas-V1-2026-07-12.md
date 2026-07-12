# Workflow Visual Canvas V1 (New System) — 2026-07-12

## Goal
Native visual canvas in New Shell for workflow definitions (stages + transitions + legend + inspect).

## V1 scope
- Native window under `SiNet.App.Wpf/Surfaces/Workflow`
- Load graphs via `IWorkflowClosedViewerQueryService` (includes `CanvasX`/`CanvasY` + TaskResult Hebrew names)
- Definition picker + nodes colored by `IsInitial`/`IsFinal`/`NodeType`
- **Drag nodes** + **שמור פריסה** → CanvasX/Y only
- **Click stage / edge** → detail panel with colored frames: Trigger (green) / Condition (blue) / Actions (purple)
- Bilingual labels: English code line + Hebrew name underneath (TaskResult from DB; Trigger/Condition/Action from static map)
- Parallel edges (same From→To) and reverse pairs are **fan-out offset** so each arrow is selectable
- Self-loops offset when multiple; orange banner explains stay-in-stage
- Seed fills Canvas only when existing coords are blank

## Bidirectional / self-loop
- No bidirectional type — A↔B = two one-way rules with reverse bias + fan-out
- Self-loop = stay in stage until another outgoing condition fires

## Later
- Catalog-bound content editing
- Promote Canvas coords into seed
- Closed Viewer tree: include self-loops
