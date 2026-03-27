# SiNet Application Logging System

## Overview

The SiNet application uses a centralized logging system controlled by user settings.
**Default state: DISABLED (OFF)** - no log files are created until the user explicitly enables logging.

## Settings Location

Logging settings are accessible via:
**Main Menu → Settings (הגדרות) → Logging Section (רישום לוג)**

### Options:
- **Enable logging checkbox** - Master on/off switch
- **Log folder picker** - Choose custom folder (or use default)
- **Test write button** - Verify write permissions
- **Open folder button** - Quick access to log directory
- **Clear old logs button** - Delete logs older than 7 days

## Log File Location

**Default location:**
```
%LocalAppData%\SiNetProjectManager\Logs\
```
Example: `C:\Users\<username>\AppData\Local\SiNetProjectManager\Logs\`

**Custom location:**
Users can specify any folder with write permissions.

## Log File Naming

Files use rolling daily naming:
```
SiNet_yyyy-MM-dd.log
```
Examples:
- `SiNet_2025-01-15.log`
- `SiNet_2025-01-16.log`

## Log Format

Each log entry follows this format:
```
[timestamp] [T###] [LEVEL] message
```

Example:
```
[2025-01-15 14:32:15.123] [T001] [INFO] Logger initialized. LogDirectory=C:\Users\danny\AppData\Local\SiNetProjectManager\Logs
[2025-01-15 14:32:16.456] [T005] [DEBUG] [R01] LoadProjectsAsync: CustomerId=NULL, ActiveOnly=True
[2025-01-15 14:32:17.789] [T005] [ERROR] Database connection failed: Connection timeout
```

### Log Levels:
- **DEBUG** - Detailed diagnostic information (only in Debug builds)
- **INFO** - General informational messages
- **WARN** - Warning conditions
- **ERROR** - Error conditions

## Technical Details

### Central Logger: `AppLogger`
Located in: `SiNetSQL\Services\AppLogger.cs`

Key APIs:
```csharp
AppLogger.Configure(enabled, logDirectory);  // Initialize at startup
AppLogger.Info("message");                    // Log info
AppLogger.Warn("message");                    // Log warning
AppLogger.Error("message");                   // Log error
AppLogger.Error(exception, "context");        // Log exception
AppLogger.Debug("message");                   // Log debug (DEBUG builds only)
```

### GoogleConnector Integration
The GoogleConnector library uses `IReportLogger` interface.
Main app wires it via `AppLoggerReportAdapter` at startup.

### Thread Safety
- All logging is thread-safe (uses lock for file writes)
- Never throws exceptions - all I/O is wrapped in try/catch

## Maintenance

### Automatic Cleanup
- Users can manually clear old logs via Settings
- Logs older than 7 days can be deleted

### Storage Considerations
- Each log file typically 1-10 MB depending on activity
- Enable only when troubleshooting issues
- Remember to disable after debugging to save disk space

## Troubleshooting

### Logs not being created?
1. Verify logging is enabled in Settings
2. Test write permissions using "בדוק כתיבה" button
3. Check folder path is valid and writable

### Finding specific issues?
1. Note the timestamp when issue occurred
2. Open the log file for that date
3. Search for `[ERROR]` entries
4. Look for stack traces after error messages
