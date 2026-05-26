# Solution Folder Structure

This document defines the preferred folder and project structure for implementation.

Copilot should follow this structure as closely as possible when adding new code.

## Existing Solution Context

Current solution contains multiple projects, including:
- WPF UI project
- Core SQL / Models / Services project
- Google connector
- Autodesk connector
- Sync engine

Implementation should fit into the existing solution, not create an unrelated structure.

---

## Recommended Logical Structure

```text
SiNetProjectManager.sln
│
├── SiNetProjectManager/              # WPF UI
│   ├── Views/
│   ├── Dialogs/
│   ├── UserControls/
│   ├── Converters/
│   ├── Behaviors/
│   ├── Resources/
│   └── App.xaml.cs
│
├── SiNetSQL/                         # Core application/domain/infrastructure logic
│   ├── Models/
│   │   ├── Core/
│   │   ├── Tasks/
│   │   ├── Email/
│   │   ├── Workflow/
│   │   ├── Files/
│   │   ├── Inspection/
│   │   └── Decisions/
│   │
│   ├── MVVM/
│   │   ├── Email/
│   │   ├── Workflow/
│   │   ├── ProjectWork/
│   │   ├── Tasks/
│   │   ├── Inspection/
│   │   └── Decisions/
│   │
│   ├── Services/
│   │   ├── Email/
│   │   ├── Workflow/
│   │   ├── Tasks/
│   │   ├── ProjectWork/
│   │   ├── Files/
│   │   ├── Inspection/
│   │   ├── Decisions/
│   │   ├── UseCases/
│   │   └── Coordinators/
│   │
│   ├── Data/
│   │   ├── Configurations/
│   │   ├── Partials/
│   │   └── SiNetSQLDbContext.cs
│   │
│   ├── DTOs/
│   │   ├── Email/
│   │   ├── Workflow/
│   │   ├── Files/
│   │   └── Common/
│   │
│   └── Helpers/
│
├── SiOffice.GoogleConnector/
│   ├── Gmail/
│   ├── Drive/
│   ├── Sheets/
│   └── Common/
│
├── SiOffice.AutodeskConnector/
│   ├── ACC/
│   ├── BIM360/
│   └── Common/
│
├── MasterPlan.SyncEngine/
│   ├── Api/
│   ├── Offline/
│   ├── Backup/
│   └── Infrastructure/
│
└── SiMasterPlanWeb/
```

---

## Folder Rules for Copilot

### Models
Put entities in `SiNetSQL/Models/` under the relevant domain folder.

Examples:
- WorkflowInstance -> `Models/Workflow/WorkflowInstance.cs`
- EmailInboxMessage -> `Models/Email/EmailInboxMessage.cs`
- ProjectAssignment -> `Models/Tasks/ProjectAssignment.cs`

### DTOs
Put transport / runtime data objects in `SiNetSQL/DTOs/`.

Examples:
- EmailContext -> `DTOs/Email/EmailContext.cs`
- SuggestedAction -> `DTOs/Email/SuggestedAction.cs`

### Services
Put business logic services in `SiNetSQL/Services/`.

Examples:
- WorkflowEngine -> `Services/Workflow/WorkflowEngine.cs`
- EmailContextAnalyzer -> `Services/Email/EmailContextAnalyzer.cs`
- FileImportCoordinator -> `Services/Coordinators/FileImportCoordinator.cs`

### ViewModels
Put ViewModels in `SiNetSQL/MVVM/` or the existing MVVM location used by the solution.

Examples:
- WorkflowDashboardViewModel
- EmailContextViewModel
- FileImportViewModel

### Views
Put WPF views in `SiNetProjectManager/Views/`, `Dialogs/`, or `UserControls/`.

Examples:
- EmailContextPanel.xaml -> `UserControls/`
- WorkflowDefinitionEditor.xaml -> `Dialogs/`

### EF Configurations
Put Fluent API configuration classes in:
- `SiNetSQL/Data/Configurations/`

Examples:
- WorkflowConfiguration.cs
- WorkflowStageDefinitionConfiguration.cs

---

## Naming Rules

1. Use clear names matching the architecture documents.
2. Use singular names for entities.
3. Use `...Service` for reusable domain/application services.
4. Use `...UseCase` only when implementing a specific business action handler.
5. Use `...Coordinator` when orchestration spans multiple modules.
6. Use `...ViewModel` only for UI-facing state.

---

## Dependency Rules

1. UI depends on MVVM and Services.
2. Services may depend on DbContextFactory, DTOs, domain models, and integration services.
3. Models must not depend on WPF.
4. Connector projects must not depend on WPF.
5. ViewModels must not contain filesystem scanning, workflow rules, or database orchestration logic.

---

## Migration Rule Reminder

Even if new entities or DbSets are added:

Copilot must NEVER create or execute EF migrations.

Migration files and actual schema changes are manual only.

---

## Implementation Strategy Reminder

When adding new code:

1. Read the architecture document.
2. Read the implementation spec.
3. Place code in the proper project/folder.
4. Follow existing conventions in the repository.
5. Avoid large god classes.
6. Prefer small services with clear responsibilities.
