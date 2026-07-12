# MaterialIntake Deep Dive — 2026-07-12

- **Status:** Remediation applied (canvas + MAT seed/seeder); Start stays `IsInitial`/`Stage` (no empty Start node)
- **Code:** `MaterialIntake` (`MAT.*`)
- **Role:** Reusable child sub-workflow hosted by Review / Planning

## 1. Stage matrix (seed truth)

| Code | Name | SortOrder | IsInitial | IsFinal | NodeType | Canvas (linear) | Tasks |
|------|------|----------:|-----------|---------|----------|-----------------|-------|
| `MAT.Receive` | קבלת חומר | 10 | yes | no | `Stage` | (40,120) | `FileInitialMaterials` → OfficeManagement |
| `MAT.File` | תיוק חומר | 20 | no | no | `Stage` | (260,120) | `FileInitialMaterials` → OfficeManagement |
| `MAT.Check` | בדיקת שלמות חומר | 30 | no | no | `Stage` | (480,120) | `CheckQuoteMaterialCompleteness` → SeniorManagement |
| `MAT.AwaitingCompletion` | ממתין להשלמת חומר חסר | 40 | no | no | `Stage` | (700,120) | `RequestMissingMaterial` → OfficeManagement |
| `MAT.Complete` | חומר הושלם | 50 | no | yes | `Stage` | (920,120) | *(none)* |

Source: `SiNetSQL/Services/SeedData/MaterialIntakeWorkflowSeedData.cs` (+ Infra mirror)

## 2. Transitions (runtime contract)

| From | To | Trigger | Condition | Eval | Actions |
|------|-----|---------|-----------|------|---------|
| Receive → File | AllRequiredTasksClosed | AllTasksComplete | Auto | — |
| File → Check | AllRequiredTasksClosed | AllTasksComplete | Auto | — |
| Check → Complete | TaskStatusChanged | TaskResultEquals=`MaterialComplete` | Auto | — |
| Check → AwaitingCompletion | TaskStatusChanged | TaskResultEquals=`MaterialMissing` | Auto | SetProjectStatus=`WaitingForMaterial` |
| AwaitingCompletion → AwaitingCompletion | TaskStatusChanged | TaskResultEquals=`MissingMaterialRequestSent` | Auto | — |
| AwaitingCompletion → Check | TaskStatusChanged | TaskResultEquals=`MissingMaterialReceived` | Auto | — |

No Decision/Fork/Join nodes — branching is multiple transitions from `MAT.Check`.

## 3. Runtime path

1. Parent (Review/Planning) reaches SubWorkflow host stage → `StartSubWorkflow` (or provisioning auto-start).
2. Cap: `Workflow.MaxOpenChildInstances` (default 2) blocks extra Active/Paused MAT instances on same project.
3. `WorkflowEngine.StartAsync` picks stage with **`IsInitial`** (`MAT.Receive`) — not `NodeType=Start`.
4. `AutoAdvancePastStartNodeAsync` is a no-op for MAT (Receive is `Stage`, not `Start`).
5. Tasks provisioned; auto-advance via triggers above.
6. On child Completed → parent notified (`SubWorkflowCompleted`).

## 4. Gaps (why canvas looked “wrong” / process felt unfinished)

| Gap | Effect | Status |
|-----|--------|--------|
| Seeds never set `NodeType=Start/End` | Canvas legend showed Start/End but all nodes painted as Stage | **Mitigated:** canvas paints Initial/Final from `IsInitial`/`IsFinal`; seed writes explicit `NodeType=Stage` |
| `CanvasX/Y` always 0 in seed | Auto 4-column grid → messy layout | **Fixed:** linear coords in MAT seed + seeder reconcile |
| Edges drawn under nodes | Arrows invisible / no direction | **Fixed:** edges + arrowheads above nodes |
| Designer vocabulary richer than seed | Decision/Fork/Join absent from MAT (by design today) | Unchanged |
| Start triggers table not driving runtime | Entry is code + IsInitial, not canvas Start metadata | By design for this slice |

## 5. Locked remediation (this slice)

1. **Canvas:** arrows above nodes + arrowheads; horizontal SortOrder layout fallback; paint Initial=green ellipse, Final=red ellipse even when NodeType=`Stage`.
2. **Seed/seeder:** reconcile explicit `NodeType=Stage` (never downgrade SubWorkflow hosts) + linear `CanvasX/Y` by SortOrder — systemic, idempotent, no manual canvas edits.
3. **Do not** insert empty `Start` node before Receive (would break task chain / AutoAdvance) until a dedicated follow-up.

## 6. Files

- Seed: `SiNetSQL/.../MaterialIntakeWorkflowSeedData.cs` (+ Infra mirror)
- Seeder: `WorkflowSeedService.SeedWorkflowDefinitionAsync` (+ `SqlWorkflowSeedService`)
- Canvas: `SiNet.App.Wpf/Surfaces/Workflow/WorkflowVisualCanvas*`
- Cap: `WorkflowOpenChildInstanceCap` + settings key `Workflow.MaxOpenChildInstances`
- Doc: this file
