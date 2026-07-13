# Workflow Management Window — Refactor outline (next)

- **Date:** 12.07.2026
- **Status:** Outline only — not an implementation plan yet
- **Depends on:** `WorkflowManagementWindow-Inventory-2026-07-12.md` (confirmed)
- **Goal:** Direction for converting the builder tree / management window without breaking seeded workflows

---

## 1. Problem statement

Today the **definition tree** lives as large imperative code-behind in `WorkflowManagementWindow`, while the **visual designer** already uses MVVM (`WorkflowDesignerViewModel`). Both mutate the same EF graph with different rules (especially Codes and Initial/Final). That makes the tree hard to test, easy to drift from the designer, and unsafe around seeded `Code` values.

---

## 2. Recommended direction (defaults)

### Phase A — Extract builder to MVVM (keep two tabs)

1. Introduce `WorkflowBuilderViewModel` (+ tree node VMs) hosting:
   - load definitions tree
   - selection → detail state
   - commands for CRUD / reorder already present in code-behind
2. Replace imperative `TreeViewItem` construction with bound `HierarchicalDataTemplate`s.
3. Keep visual designer tab as-is initially; both call a shared application service for persistence (new or thin wrapper over existing DbContext usage).

**Why this first:** Matches inventory finding #2 (no ViewModel); lowest risk vs merging UIs.

### Phase B — System-code locks in UI

1. Treat Codes in `WorkflowCodes` + `*StageCodes` as **system** (read-only in Builder and Designer).
2. Allow rename of display Name/Description freely; never rewrite system Codes via `GenerateCode`.
3. Optional later: DB `IsSystem` or config list — not required for first pass if constants are the source of truth.

### Phase C — Align Builder ↔ Designer rules

| Rule | Target behavior |
|------|-----------------|
| IsInitial / IsFinal | Single policy (prefer explicit Designer flags **or** SortOrder-derived — pick one and enforce in shared service) |
| Transition edit | Shared validation (cycles, missing targets, SubWorkflow constraints) |
| Save | Same upsert semantics; avoid Designer “full replace” silently dropping Builder-only fields |

### Phase D — Unification (optional, later)

Only after A–C: decide whether the canvas becomes the primary editor and the tree becomes a read-only navigator (or vice versa). **Do not merge UIs in the first refactor slice.**

---

## 3. Explicit non-goals (first refactor slices)

- Rewriting seed graphs / changing seeded Codes
- Moving ProcessAction handlers or TaskInteraction registry into admin UI
- Merging instance board / ProjectType policy / task behaviors into the builder VM
- Migrating the window into `SiNet.App.Wpf` New System (separate program of work)

---

## 4. Suggested delivery slices

| Slice | Deliverable | Exit criteria |
|-------|-------------|----------------|
| A1 | `WorkflowBuilderViewModel` + read-only bound tree | Tree matches current hierarchy; refresh works |
| A2 | Detail panel bound for Workflow / Stage | Save/delete/reorder parity with today |
| A3 | Transition + StageTask detail + commands | Full builder parity; code-behind CRUD removed |
| B1 | System Code read-only in Builder | Seeded Codes cannot be changed from UI |
| B2 | Same lock in Designer | Codes of seeded stages/defs locked |
| C1 | Shared `IWorkflowDefinitionEditorService` | Both tabs persist through one API |

---

## 5. Risks to manage

- **Seed additive behavior:** UI must not assume re-seed cleans designer extras.
- **Active instances:** keep delete-guard; prefer `IsActive = false`.
- **SubWorkflow stages:** no stage-task provisioning; validation must stay.
- **Duplicate surfaces:** until C1, double-edit race between tabs if both open (today: same window, sequential tabs — still warn on unsaved Designer state).

---

## 6. Decision checklist before coding

Answer these in the next planning session (implementation plan), then implement:

1. Confirm Phase A (MVVM builder, keep Designer tab) as the first coding target.
2. Confirm system Codes = constant lists (no DB flag yet).
3. Confirm Initial/Final policy: keep SortOrder-derived for Builder, or move both to explicit flags.
4. Confirm whether New System (`SiNet.App.Wpf`) is in or out of this program.

---

## 7. References

- Inventory: `WorkflowManagementWindow-Inventory-2026-07-12.md`
- UI map: `WorkflowUiScreens-2026-06-21.md`
- Codes: `SiNet.Infrastructure.Sql/Constants/WorkflowCodes.cs`, `*StageCodes.cs`
- Seed: `SqlWorkflowSeedService.cs`

