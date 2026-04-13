
# SYSTEM_MASTER_SPEC.md
## Master System Specification for Copilot Implementation

This document is the **single authoritative specification** describing the system to be implemented.
It replaces all earlier scattered specification documents and consolidates:

- Original system specifications
- Architecture decisions
- Domain definitions
- Workflow design
- ProjectWork workspace design
- Email processing logic
- Review / Inspection system
- File management and versioning
- Implementation rules for Copilot

The goal of this document is to allow **Copilot to implement the system directly from this specification**.

---

# 1. System Overview

The system is an **internal engineering project management and execution platform**.

It is not just an email tool.  
It is a **Project Work Operating System** that integrates:

- communication
- process management
- file management
- engineering workflows
- inspection / review cycles

The core flow of the system is:

Email → Context → Workflow → Work → Files → Review → Delivery

Where:

Email = communication trigger  
Context = understanding project meaning of the email  
Workflow = business process  
Task = executable unit of work  
ProjectWork = engineering workspace  
Files = project material  
Review = engineering inspection process

---

# 2. Technology Stack

The system is implemented using:

- Visual Studio
- C#
- WPF
- SQL Server
- Entity Framework Core

Key integrations:

- Google Gmail
- Autodesk ACC / BIM360
- Local filesystem storage

The system architecture follows layered design:

UI (WPF)
Application (Use Cases)
Domain (Business logic)
Infrastructure (DB / Filesystem / integrations)

---

# 3. Core System Modules

The system contains the following modules.

## 3.1 Email Module

Responsible for:

- ingesting emails
- detecting attachments
- detecting links to files
- associating email threads with projects
- suggesting actions based on context

Entities:

EmailInboxMessage  
EmailInboxAttachment  
ThreadStatusMapping  
EmailAssociation  
EmailUserOverride

Key behavior:

Email is **not the working environment**.

Email acts as the **trigger that starts work**.

Association levels:

Thread level  
Message level  
User override

---

## 3.2 Project Module

The **Project entity is the main business anchor**.

Project contains:

ProjectId  
ProjectNumber  
ProjectTypes  
ProjectStatus  
Participants  
Decisions  
Folders and files  
Workflow instances

Important rule:

A project can have **multiple project types simultaneously**.

Examples:

Design  
Review  
Opinion

---

## 3.3 Workflow Module

Workflow represents **long running engineering processes**.

Examples:

Design Workflow  
Review Workflow  
Opinion Workflow  
Scope Expansion Workflow  
Email Intake Workflow

Core entities:

WorkflowDefinition  
WorkflowStageDefinition  
WorkflowTransitionRule  
WorkflowInstance  
WorkflowStageTransition

Workflow controls:

- stage transitions
- process lifecycle
- process history

Important:

Workflow ≠ Task.

Workflow is the **process lifecycle**.

---

## 3.4 Task Module

Tasks represent **units of execution**.

Entities:

ProjectAssignment  
TaskType  
TaskStatus  
TaskPriority  
TaskEvent  
TaskLink

Design rule:

The system must **not create thousands of tasks**.

Usually:

One main task per process stage.

Tasks are used to drive execution but not represent the entire process.

---

## 3.5 ProjectWork Workspace

ProjectWork is the **central workspace where engineers actually work**.

Capabilities:

Unified project file tree  
Integration of DB metadata + filesystem files  
File naming convention analysis  
Alternatives management  
Version management  
Drag and drop file operations  
External program launching  
ACC integration  
FileSystemWatcher

Document model:

ProjectDocument
    Alternative
        Version

Important rule:

Database stores **metadata only**.

Filesystem stores **physical files**.

---

## 3.6 File Management Module

Responsible for:

Project documents  
File versions  
Imported attachments  
External files  
ACC storage  
Local storage

Entities:

ProjectFolder  
ProjectFile  
ProjectFileRef  
DocumentAlternative  
DocumentVersion

Naming convention is critical for identifying files.

---

## 3.7 Review / Inspection Module

Used for engineering inspection processes.

Entities:

InspectionSeries  
InspectionReport  
Chapter  
Section  
InspectionNote  
InspectionNoteStatus  
CommentsBank

Capabilities:

Review rounds  
Checklist driven inspection  
Comment management  
Report generation  
Carry over between review rounds

---

## 3.8 Decision Module

Responsible for managerial and engineering decisions.

Entities:

ProjectDecision  
DecisionCategory  
DecisionHistory

Decisions may originate from:

Email  
Workflow stage  
User action

Decisions must not be collapsed into tasks.

---

# 4. Domain Relationships

Project relationships:

Project → many WorkflowInstances  
Project → many ProjectAssignments  
Project → many ProjectFiles  
Project → many InspectionSeries  
Project → many ProjectDecisions

Email relationships:

EmailInboxMessage → many EmailInboxAttachment  
EmailInboxMessage → may start Workflow  
EmailInboxMessage → may create Task  
EmailInboxMessage → may create Decision

Workflow relationships:

WorkflowInstance → one WorkflowDefinition  
WorkflowInstance → many WorkflowStageTransitions  
WorkflowInstance → may create Tasks

---

# 5. Email Context Engine

The Email Context Engine analyzes each email to determine its relevance.

Input:

EmailInboxMessageId

Output:

EmailContext object containing:

Detected project  
Active workflows  
Related tasks  
Related decisions  
Attachment analysis  
Context confidence score

The engine must analyze:

Thread association  
Project participants  
File attachments  
Workflow state

---

# 6. Suggested Actions Engine

Based on EmailContext the system proposes actions.

Possible actions:

StartWorkflow  
AttachToWorkflow  
CreateTask  
ImportFiles  
CreateDecision  
StartReview  
UploadToACC

Actions are suggestions only.

User always confirms execution.

---

# 7. File Import Pipeline

Responsible for importing files from email into the project workspace.

Process:

1. Detect attachment
2. Determine project
3. Generate file naming convention
4. Copy file into project folder
5. Register metadata in DB
6. Refresh ProjectWork tree

---

# 8. Implementation Rules for Copilot

Copilot must follow these rules.

1. Do not redesign architecture.
2. Implement modules defined here.
3. Keep ViewModels thin.
4. Put business logic in services and use cases.
5. Follow module boundaries.
6. Respect domain relationships.

Important database rule:

Copilot must NEVER execute or create EF migrations automatically.

If schema changes are required:

Copilot must describe them but migrations are created manually by developers.

---

# 9. Implementation Order

Copilot should implement the system in the following order:

1. Workflow foundation
2. Workflow engine
3. Email context engine
4. Suggested actions engine
5. File import pipeline
6. ProjectWork integration
7. Review module
8. ACC integration

---

# 10. System Vision

The final goal is to build a **single integrated workspace for engineering work**.

Engineers should be able to:

Receive project communication  
Understand the context  
Run project workflows  
Work with project files  
Perform engineering reviews  
Deliver project outputs

All inside one unified system.
