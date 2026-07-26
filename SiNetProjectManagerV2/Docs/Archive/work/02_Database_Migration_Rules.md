# Database Migration Rules

## IMPORTANT RULE

Copilot must NEVER execute or generate automatic database migrations.

All database schema migrations are handled manually by the development team.

## Copilot Responsibilities

Copilot may:

- Suggest new Entities
- Suggest DbSet additions
- Suggest table structures
- Suggest configuration classes

But Copilot must NOT:

- Run `Add-Migration`
- Run `Update-Database`
- Modify the database schema automatically
- **Create, edit, rewrite, patch, or delete any file under `Migrations/`**
  (including Designer and ModelSnapshot). Existing migration files are immutable.

## If Schema Changes Are Required

Copilot must:

1. Describe the required schema change.
2. Provide the Entity and EF configuration.
3. Clearly state:

"Database migration must be created manually by the developer."

4. Give the developer the `dotnet ef migrations add` / `database update` commands.
5. **Stop.** Do not touch migration files even if apply fails — diagnose only;
   the developer chooses the fix.

Example:

Required table: WorkflowInstances

Fields:
- Id
- WorkflowDefinitionId
- ProjectId
- Status
- CurrentStageId

Migration must be created manually.