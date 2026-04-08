# Domain Model Map

This document defines the core domain objects and relationships that Copilot must respect during implementation.

## Core Principle

The system is built around:

- Project = business anchor
- Email = trigger
- Workflow = process
- Task = unit of work
- ProjectWork = execution workspace
- Files = working material
- Review = inspection / control
- Decision = management / approval layer

---

## Main Domain Areas

### 1. Project Domain
Core business entity.

Entities:
- Project
- ProjectTypeAssignment
- ProjectStatus
- ProjectParticipant
- ProjectDecision
- ProjectFolder
- ProjectFile
- ProjectFileRef

Notes:
- A Project may have multiple project types.
- Project is the root business context for most operations.

---

### 2. Email Domain
Responsible for email intake and association.

Entities:
- EmailInboxMessage
- EmailInboxAttachment
- ThreadStatusMapping
- EmailAssociation
- EmailUserOverride

Runtime DTOs:
- EmailContext
- SuggestedAction

Notes:
- Email is a trigger, not the execution workspace.
- Email association may exist at thread level, message level, and user override level.

---

### 3. Workflow Domain
Responsible for long-running business processes.

Entities:
- WorkflowDefinition
- WorkflowStageDefinition
- WorkflowTransitionRule
- WorkflowInstance
- WorkflowStageTransition

Enums:
- WorkflowStatus
- WorkflowTriggerType

Notes:
- Workflow is NOT the same as Task.
- Workflow manages business stage progression.

---

### 4. Task Domain
Responsible for executable work units.

Entities:
- ProjectAssignment
- TaskType
- ProjectAssignmentStatus
- ProjectAssignmentEvent
- TaskLink

Notes:
- Tasks are execution units.
- Not every workflow transition must create a task.
- TaskLink is the universal cross-module connector.

---

### 5. File Domain
Responsible for project files, versions, and physical file placement tracking.

Entities:
- ProjectFolder
- ProjectFile
- ProjectFileRef
- ProjectFileInstance (physical file placement tracker)
- ProjectAlternative
- DocumentAlternative
- DocumentVersion

Enums:
- FileStorageDestination (FileServer=0, Acc=1)
- FileInstanceSourceType (Manual=0, EmailAttachment=1, Template=2)

Runtime Objects:
- BaseFileVersion

Notes:
- DB stores metadata.
- Filesystem / ACC stores actual files.
- ProjectFile.StorageDestination controls routing (FileServer vs ACC).
- ProjectFileInstance records every physical file placement with source tracking.
- Naming convention is central.

---

### 6. Review Domain
Responsible for inspection and review cycles.

Entities:
- InspectionSeries
- InspectionReport
- Chapter
- Section
- InspectionNote
- InspectionNoteStatus
- CommentsBank

Notes:
- Review is a separate lifecycle, not just a task type.

---

### 7. Decision Domain
Responsible for business / managerial decisions.

Entities:
- ProjectDecision
- DecisionCategory
- DecisionHistory

Notes:
- Decisions are not tasks.
- Decisions may be linked to email, workflow, task, or project.

---

## Main Relationships

### Project relationships
- Project -> many ProjectTypeAssignment
- Project -> many ProjectFolder
- ProjectFolder -> many ProjectFile
- ProjectFile -> many ProjectFileRef
- ProjectFile -> many ProjectFileInstance
- Project -> many ProjectAssignment
- Project -> many WorkflowInstance
- Project -> many ProjectDecision
- Project -> many InspectionSeries

### Email relationships
- EmailInboxMessage -> many EmailInboxAttachment
- EmailInboxAttachment -> optional ProjectFileInstance (FK)
- EmailInboxMessage -> may link to Project
- EmailInboxMessage -> may trigger WorkflowInstance
- EmailInboxMessage -> may create Task
- EmailInboxMessage -> may create Decision

### Workflow relationships
- WorkflowDefinition -> many WorkflowStageDefinition
- WorkflowDefinition -> many WorkflowTransitionRule
- WorkflowInstance -> one WorkflowDefinition
- WorkflowInstance -> many WorkflowStageTransition
- WorkflowInstance -> may spawn Tasks
- WorkflowInstance -> may link to Files
- WorkflowInstance -> may link to Emails

### Task relationships
- ProjectAssignment -> one Project
- ProjectAssignment -> one TaskType
- ProjectAssignment -> one Status
- ProjectAssignment -> many Events
- ProjectAssignment -> many TaskLinks

### Review relationships
- InspectionSeries -> many InspectionReport
- InspectionReport -> many Chapters
- Chapter -> many Sections
- Section -> many InspectionNotes

---

## Architectural Rules

1. Project remains the main business anchor.
2. Email must never become the root of the whole system.
3. Workflow lifecycle must be modeled separately from task status.
4. Files must remain separated into:
   - metadata in DB (ProjectFile = definition, ProjectFileInstance = placement record)
   - physical files in filesystem (FileServer) or ACC (Autodesk)
   - StorageDestination on ProjectFile controls routing
5. Review must remain an independent bounded area.
6. Decisions must not be collapsed into tasks.
7. ViewModels must not contain domain rules.

---

## Copilot Implementation Reminder

When implementing code:

- Always check which domain area the change belongs to.
- Place classes in the correct module.
- Do not merge Workflow and Task into one concept.
- Do not treat ProjectWork as just a file explorer.
- Do not generate migrations automatically.
