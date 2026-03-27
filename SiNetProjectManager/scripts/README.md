# Database Migration Deployment Guide

## Overview

This project uses **EF Core Migration Bundles** for database schema deployment.
Automatic migrations (`Database.Migrate()`) are **disabled** for production safety.

## Prerequisites

- .NET 8 SDK installed
- EF Core Tools installed:
  ```powershell
  dotnet tool install --global dotnet-ef
  ```

## Automatic Bundle Generation (Recommended)

The EF Migration Bundle is **automatically built during Publish**.

### Using Visual Studio Publish

1. Right-click the `SiNetProjectManager` project
2. Select "Publish..."
3. Configure your publish profile
4. Click "Publish"
5. The `efbundle.exe` will be included in the publish output

### Using Command Line

```powershell
dotnet publish SiNetProjectManager\SiNetProjectManager.csproj -c Release -r win-x64 --self-contained
```

The `efbundle.exe` will be in the publish directory alongside the application.

## Manual Bundle Generation

If you need to build the bundle separately:

### From Solution Root

```powershell
.\scripts\build_efbundle.ps1
```

### Direct Command

```powershell
dotnet ef migrations bundle `
    --project "..\SiNetSQL\SiNetSQL\SiNetSQL.csproj" `
    --startup-project "SiNetProjectManager\SiNetProjectManager.csproj" `
    --context SiNetSQLDbContext `
    --runtime win-x64 `
    --output efbundle.exe `
    --force
```

## Deployment Steps

### 1. Deploy Application

Copy the entire publish folder to the target server.

### 2. Apply Migrations (First Time / After Updates)

Run the migration bundle with the target connection string:

```powershell
.\efbundle.exe --connection "Data Source=SERVER;Initial Catalog=DATABASE;Integrated Security=True;TrustServerCertificate=True;"
```

Or use the helper script:

```powershell
.\scripts\run_efbundle.ps1 -ConnectionString "your-connection-string"
```

### 3. Start Application

The application will:
1. Check if required tables exist
2. If missing, show an error message (run efbundle.exe)
3. If present, seed baseline data and start normally

## Development Workflow

### Creating a New Migration

1. Make changes to EF models in `SiNetSQL`
2. Generate migration:
   ```powershell
   Add-Migration MigrationName -Project SiNetSQL -StartupProject SiNetProjectManager
   ```
3. Review the generated migration file
4. Build and test locally
5. Commit the migration files

### Applying Migrations (Development)

For development, you can use the PMC:
```powershell
Update-Database -Project SiNetSQL -StartupProject SiNetProjectManager
```

Or use the bundle scripts for consistency with production.

## Application Startup Behavior

The application checks for required database schema at startup:

1. **Connection Check**: Verifies database is accessible
2. **Schema Check**: Verifies Task Management tables exist
3. **Seed Data**: Inserts baseline data (idempotent)

If schema is missing, the application shows an error and exits gracefully:
```
מבנה מסד הנתונים אינו עדכני.
יש להריץ את efbundle.exe לעדכון המבנה.
```

## Required Tables (Task Management)

The following tables must exist for the application to start:

| Table | Description |
|-------|-------------|
| `TaskType` | Task type lookup (כללי, תכנון במשרד, בדיקת תוכנית) |
| `ProjectAssignmentStatus` | Task status lookup (פתוח, הושלם, ממתין לגורם חיצוני) |
| `ProjectAssignmentEvent` | Task event log |
| `UserSetting` | Per-user settings |

## Troubleshooting

### "Database schema is outdated"

Run `efbundle.exe` with the correct connection string.

### "Cannot connect to database"

1. Verify server is running
2. Check connection string
3. Verify network access
4. Check Windows authentication / credentials

### Migration bundle build fails

1. Ensure `dotnet ef` is installed
2. Build the solution first: `dotnet build`
3. Check for compilation errors

## Files

| File | Purpose |
|------|---------|
| `scripts\build_efbundle.ps1` | Creates the migration bundle |
| `scripts\run_efbundle.ps1` | Executes the bundle |
| `efbundle.exe` | Self-contained migration executable |

## Security Notes

- The bundle does NOT contain connection strings
- Connection strings are passed at runtime
- Use Windows Authentication when possible
- Never commit connection strings to source control
