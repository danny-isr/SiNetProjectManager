using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNet.Infrastructure.Sql.Services.SeedData;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Service responsible for seeding Task Management baseline data.
/// 
/// IMPORTANT SEEDING RULES:
/// 
/// 1. STATIC LOOKUP TABLES (TaskTypes, ProjectAssignmentStatuses):
///    - Seeded on app startup
///    - "Create missing only" mode - NEVER updates or overwrites existing rows
///    - Safe to add new defaults, but user modifications are preserved
/// 
/// 2. EDITABLE CONFIGURATION TABLES (ProjectTypeTaskType, ProjectTypeStatus mappings):
///    - NOT seeded automatically on startup
///    - Can only be reset via explicit "Restore Defaults" action
///    - User/admin changes are preserved until explicit reset
/// 
/// All seed data is defined in the SeedData folder:
/// - TaskTypeSeedData
/// - ProjectAssignmentStatusSeedData
/// - ProjectTypeTaskTypeMappingSeedData
/// - ProjectTypeStatusMappingSeedData
/// </summary>
public class SqlTaskManagementSeedService
{
    private readonly SiNetSQLDbContext _context;
    private static bool _seedingCompleted = false;
    private static readonly object _lock = new object();

    public SqlTaskManagementSeedService(SiNetSQLDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    #region Startup Seeding (Static Lookups Only)

    /// <summary>
    /// Ensures static lookup data exists on app startup.
    /// Only creates missing rows - NEVER updates or overwrites existing data.
    /// Does NOT seed editable configuration (ProjectType mappings).
    /// Thread-safe and idempotent.
    /// </summary>
    public void EnsureStaticLookupData()
    {
        // Fast path: if already seeded in this app session, skip
        if (_seedingCompleted)
            return;

        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_seedingCompleted)
                return;

            try
            {
                DevToolsLog.Info("[SeedService] Starting static lookup seeding (create missing only)...");
                
                var taskTypesInserted = EnsureTaskTypes_CreateMissingOnly();
                var statusesInserted = EnsureStatuses_CreateMissingOnly();
                var projectStatusesInserted = ReconcileProjectStatusesToCanonical();
                var taskResultsInserted = EnsureTaskResultDefinitions_CreateMissingOnly();
                var (inspectionStatusesInserted, inspectionStatusesUpdated) = UpsertInspectionNoteStatuses();

                _seedingCompleted = true;

                if (taskTypesInserted > 0 || statusesInserted > 0 || projectStatusesInserted > 0 || taskResultsInserted > 0 || inspectionStatusesInserted > 0 || inspectionStatusesUpdated > 0)
                {
                    DevToolsLog.Info($"[SeedService] Static lookup seeding complete: {taskTypesInserted} TaskTypes, {statusesInserted} Statuses, {projectStatusesInserted} ProjectStatuses, {taskResultsInserted} TaskResultDefinitions created, InspectionNoteStatuses: {inspectionStatusesInserted} inserted / {inspectionStatusesUpdated} updated");
                }
                else
                {
                    DevToolsLog.Info("[SeedService] Static lookup seeding complete: All data already exists");
                }
            }
            catch (Exception ex)
            {
                DevToolsLog.Error(ex, "[SeedService] Error during static lookup seeding");
                throw;
            }
        }
    }

    /// <summary>
    /// Async version of EnsureStaticLookupData.
    /// </summary>
    public async Task EnsureStaticLookupDataAsync(CancellationToken cancellationToken = default)
    {
        if (_seedingCompleted)
            return;

        await Task.Run(() => EnsureStaticLookupData(), cancellationToken);
    }

    /// <summary>
    /// Seeds TaskType table - CREATE MISSING ONLY mode.
    /// Never updates or overwrites existing rows.
    /// Uses Code as the stable unique key for dedup.
    /// </summary>
    private int EnsureTaskTypes_CreateMissingOnly()
    {
        var definitions = TaskTypeSeedData.Definitions;
        var existingCodes = _context.TaskTypes
            .Select(t => t.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;

        foreach (var def in definitions)
        {
            if (!existingCodes.Contains(def.Code))
            {
                _context.TaskTypes.Add(new TaskType
                {
                    Code = def.Code,
                    Name = def.Name,
                    IsActive = def.IsActive,
                    SortOrder = def.SortOrder
                });
                existingCodes.Add(def.Code);
                inserted++;
            }
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] TaskTypes: Created {inserted} missing entries");
        }

        // Patch existing rows that have no Code yet (one-time migration)
        inserted += PatchMissingTaskTypeCodes();

        return inserted;
    }

    /// <summary>
    /// One-time idempotent patch: populates Code for existing TaskType rows
    /// that were created before the Code field existed.
    /// Detects rows by Name → Code mismatch (catches auto-generated GUIDs, empty, or wrong values).
    /// </summary>
    private int PatchMissingTaskTypeCodes()
    {
        var nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["כללי"]           = "General",
            ["תכנון במשרד"]    = "OfficePlanning",
            ["בדיקת תוכנית"]   = "PlanReview",
            ["תיוק חומר"]      = "MaterialFiling",
            ["בדיקה מקצועית"]  = "ProfessionalReview",
        };

        var allRows = _context.TaskTypes.ToList();
        int patched = 0;

        foreach (var row in allRows)
        {
            if (nameToCode.TryGetValue(row.Name, out var expectedCode))
            {
                if (!string.Equals(row.Code, expectedCode, StringComparison.Ordinal))
                {
                    row.Code = expectedCode;
                    patched++;
                }
            }
            else if (string.IsNullOrEmpty(row.Code) || row.Code.Length > 40)
            {
                // Unknown type with empty/GUID Code — generate a Code from the Name
                row.Code = row.Name.Replace(" ", "");
                patched++;
                DevToolsLog.Warn($"[SeedService] TaskType '{row.Name}' — generated fallback Code '{row.Code}'");
            }
        }

        if (patched > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] TaskTypes: Patched Code on {patched} existing rows");
        }

        return patched;
    }

    /// <summary>
    /// Seeds ProjectAssignmentStatus table - CREATE MISSING ONLY mode.
    /// Never updates or overwrites existing rows (names, sort order).
    /// After creation, reconciles IsOpen and IsActionable flags against seed definitions
    /// to ensure correct 3-state semantics (Active / Waiting / Closed).
    /// </summary>
    private int EnsureStatuses_CreateMissingOnly()
    {
        var definitions = ProjectAssignmentStatusSeedData.Definitions;
        var existingStatuses = _context.ProjectAssignmentStatuses.ToList();
        var existingCodes = existingStatuses
            .Where(s => !string.IsNullOrEmpty(s.Code))
            .Select(s => s.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNames = existingStatuses.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;

        foreach (var def in definitions)
        {
            // Match by stable Code first, then fall back to Name for legacy rows
            if (existingCodes.Contains(def.Code) || existingNames.Contains(def.Name))
                continue;

            _context.ProjectAssignmentStatuses.Add(new ProjectAssignmentStatus
            {
                Code = def.Code,
                Name = def.Name,
                IsOpen = def.IsOpen,
                IsActionable = def.IsActionable,
                SortOrder = def.SortOrder
            });
            existingCodes.Add(def.Code);
            existingNames.Add(def.Name);
            inserted++;
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] ProjectAssignmentStatuses: Created {inserted} missing entries");
        }

        // ── Reconcile IsOpen / IsActionable flags for existing statuses ──
        // These flags define system-level semantics (work queue, filtering) and must
        // stay aligned with the seed definitions even after DB recreation.
        int reconciled = 0;
        foreach (var def in definitions)
        {
            var existing = existingStatuses.FirstOrDefault(
                s => (!string.IsNullOrEmpty(s.Code) && string.Equals(s.Code, def.Code, StringComparison.OrdinalIgnoreCase))
                  || string.Equals(s.Name, def.Name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
                continue; // newly inserted above — already correct

            if (string.IsNullOrEmpty(existing.Code))
            {
                existing.Code = def.Code;
                reconciled++;
            }

            if (existing.IsOpen != def.IsOpen || existing.IsActionable != def.IsActionable)
            {
                existing.IsOpen = def.IsOpen;
                existing.IsActionable = def.IsActionable;
                reconciled++;
            }
        }

        if (reconciled > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] ProjectAssignmentStatuses: Reconciled IsOpen/IsActionable for {reconciled} entries");
        }

        return inserted;
    }

    /// <summary>
    /// Reconciles legacy <see cref="Models.ProjectStatus"/> rows to the canonical taxonomy.
    /// <list type="number">
    ///   <item>For each existing row matched by legacy Title (or already by canonical Code),
    ///         update <c>Code</c>, <c>Title</c>, <c>SortOrder</c>, <c>IsActive=true</c> in place
    ///         so existing <c>Project.ProjectStatusId</c> references stay valid.</item>
    ///   <item>Rows that don't match any mapping are deactivated (<c>IsActive=false</c>) and
    ///         left intact, so legacy projects keep a valid FK target but the status no longer
    ///         appears in active UI lists.</item>
    ///   <item>Insert canonical rows that don't exist yet (matched by Code).</item>
    /// </list>
    /// </summary>
    private int ReconcileProjectStatusesToCanonical()
    {
        // Legacy Hebrew Title → canonical Code.
        // Anything not listed here is treated as unmapped and deactivated.
        var legacyTitleToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["איסוף חומר להצעת מחיר"] = ProjectStatusCodes.QuotePreparation,
            ["הצעת מחיר"]             = ProjectStatusCodes.WaitingForQuoteApproval,
            ["בטיפול"]                = ProjectStatusCodes.Active,
            ["בהמתנה"]                = ProjectStatusCodes.WaitingForClient,
            ["הסתיים"]                = ProjectStatusCodes.Closed,
            ["הצעה לא מאושרת"]         = ProjectStatusCodes.ClosedLost,
            ["לא פרויקט תכנוני"]        = ProjectStatusCodes.Cancelled,
        };

        var canonicalByCode = ProjectStatusSeedData.Definitions
            .ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

        var existing = _context.ProjectStatuses.ToList();
        int updated = 0, deactivated = 0, inserted = 0;

        // ── Step 1: Resolve target Code for each existing row ────────────
        // Priority: already-canonical Code > legacy Title mapping.
        var rowsAdoptingCode = new Dictionary<int, string>(); // existing.Id → target canonical Code
        foreach (var row in existing)
        {
            if (!string.IsNullOrEmpty(row.Code) && canonicalByCode.ContainsKey(row.Code))
            {
                rowsAdoptingCode[row.Id] = row.Code;
                continue;
            }

            var titleKey = (row.Title ?? string.Empty).Trim();
            if (legacyTitleToCode.TryGetValue(titleKey, out var mappedCode))
            {
                rowsAdoptingCode[row.Id] = mappedCode;
            }
        }

        // Guard: if two existing rows would adopt the same canonical Code, keep the first
        // (lowest Id) and treat the rest as unmapped — UNIQUE Code must be preserved.
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in rowsAdoptingCode.OrderBy(k => k.Key).ToList())
        {
            if (!seenCodes.Add(kvp.Value))
                rowsAdoptingCode.Remove(kvp.Key);
        }

        // ── Step 2: Update mapped rows in place; deactivate unmapped rows ─
        foreach (var row in existing)
        {
            if (rowsAdoptingCode.TryGetValue(row.Id, out var targetCode))
            {
                var def = canonicalByCode[targetCode];
                bool changed = false;
                if (!string.Equals(row.Code, def.Code, StringComparison.Ordinal))   { row.Code = def.Code; changed = true; }
                if (!string.Equals(row.Title, def.Title, StringComparison.Ordinal)) { row.Title = def.Title; changed = true; }
                if (row.SortOrder != def.SortOrder)                                  { row.SortOrder = def.SortOrder; changed = true; }
                if (!row.IsActive)                                                   { row.IsActive = true; changed = true; }
                if (changed) updated++;
            }
            else
            {
                // Unmapped legacy row: keep for FK integrity, deactivate so it stops showing in UI.
                if (row.IsActive)
                {
                    row.IsActive = false;
                    deactivated++;
                }
            }
        }

        if (updated > 0 || deactivated > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] ProjectStatuses: Reconciled {updated} legacy→canonical, deactivated {deactivated} unmapped");
        }

        // ── Step 3: Insert canonical rows still missing ──────────────────
        var presentCodes = _context.ProjectStatuses
            .Select(s => s.Code)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var def in ProjectStatusSeedData.Definitions)
        {
            if (presentCodes.Contains(def.Code)) continue;

            _context.ProjectStatuses.Add(new ProjectStatus
            {
                Code = def.Code,
                Title = def.Title,
                SortOrder = def.SortOrder,
                IsActive = true,
                Created = DateTime.UtcNow
            });
            inserted++;
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] ProjectStatuses: Created {inserted} missing canonical entries");
        }

        return inserted;
    }

    /// <summary>
    /// Seeds <see cref="TaskResultDefinition"/> rows - CREATE MISSING ONLY mode.
    /// Matches existing rows by stable <c>Code</c> (case-insensitive) and inserts only missing
    /// canonical results. Also reconciles <c>Name</c>, <c>Category</c>, <c>SortOrder</c>, and
    /// <c>IsActive=true</c> to canonical values for already-existing rows so the lookup stays
    /// aligned with <see cref="TaskResultDefinitionSeedData"/>.
    /// </summary>
    private int EnsureTaskResultDefinitions_CreateMissingOnly()
    {
        var definitions = TaskResultDefinitionSeedData.Definitions;
        var existing = _context.TaskResultDefinitions.ToList();
        var existingByCode = existing
            .Where(t => !string.IsNullOrEmpty(t.Code))
            .ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);

        int inserted = 0;
        int reconciled = 0;

        foreach (var def in definitions)
        {
            if (existingByCode.TryGetValue(def.Code, out var row))
            {
                if (row.Name != def.Name || row.Category != def.Category
                    || row.SortOrder != def.SortOrder || !row.IsActive)
                {
                    row.Name = def.Name;
                    row.Category = def.Category;
                    row.SortOrder = def.SortOrder;
                    row.IsActive = true;
                    reconciled++;
                }
                continue;
            }

            _context.TaskResultDefinitions.Add(new TaskResultDefinition
            {
                Code = def.Code,
                Name = def.Name,
                Category = def.Category,
                SortOrder = def.SortOrder,
                IsActive = true,
            });
            inserted++;
        }

        if (inserted > 0 || reconciled > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] TaskResultDefinitions: Created {inserted}, Reconciled {reconciled}");
        }

        return inserted;
    }

    /// <summary>
    /// Idempotent upsert for <see cref="Models.InspectionNoteStatus"/> rows used by the
    /// inspection-note status ComboBox.
    /// <list type="bullet">
    ///   <item>Business key is <c>StatusKey</c>.</item>
    ///   <item>If the key exists, <c>HebrewLabel</c>, <c>SortOrder</c>, and <c>IsActive</c> are updated to the seeded values.</item>
    ///   <item>If the key does not exist, a new row is inserted.</item>
    ///   <item>Existing rows whose key is not in the seed list are NOT deleted.</item>
    ///   <item>
    ///     Cleanup: parallel/legacy keys that duplicate the meaning of the canonical seed
    ///     (currently <c>Comment</c> ≡ <c>Failed</c> and <c>RecurringComment</c> ≡ <c>RecurringFailed</c>)
    ///     are removed if unreferenced, or deactivated if any note still references them,
    ///     so the ComboBox does not show duplicate entries.
    ///   </item>
    /// </list>
    /// </summary>
    private (int inserted, int updated) UpsertInspectionNoteStatuses()
    {
        var definitions = InspectionNoteStatusSeedData.Definitions;
        var existing = _context.InspectionNoteStatuses.ToList();
        var byKey = existing.ToDictionary(s => s.StatusKey, StringComparer.Ordinal);

        int inserted = 0;
        int updated = 0;

        foreach (var def in definitions)
        {
            if (byKey.TryGetValue(def.StatusKey, out var row))
            {
                if (row.HebrewLabel != def.HebrewLabel
                    || row.SortOrder != def.SortOrder
                    || row.IsActive != def.IsActive)
                {
                    row.HebrewLabel = def.HebrewLabel;
                    row.SortOrder = def.SortOrder;
                    row.IsActive = def.IsActive;
                    updated++;
                }
                continue;
            }

            _context.InspectionNoteStatuses.Add(new InspectionNoteStatus
            {
                StatusKey = def.StatusKey,
                HebrewLabel = def.HebrewLabel,
                SortOrder = def.SortOrder,
                IsActive = def.IsActive,
            });
            inserted++;
        }

        // Remove parallel keys that represent the same meaning as canonical ones.
        // We never refactor existing code that uses Failed/RecurringFailed, so the
        // alternate Comment/RecurringComment keys must not appear in the dropdown.
        var parallelKeys = new[] { "Comment", "RecurringComment" };
        foreach (var parallelKey in parallelKeys)
        {
            if (!byKey.TryGetValue(parallelKey, out var parallelRow))
                continue;

            var inUse = _context.InspectionNotes.Any(n => n.NoteStatusId == parallelRow.StatusId);
            if (!inUse)
            {
                _context.InspectionNoteStatuses.Remove(parallelRow);
                DevToolsLog.Info($"[SeedService] InspectionNoteStatuses: Removed parallel key '{parallelKey}' (unused).");
                updated++;
            }
            else if (parallelRow.IsActive)
            {
                parallelRow.IsActive = false;
                DevToolsLog.Info($"[SeedService] InspectionNoteStatuses: Deactivated parallel key '{parallelKey}' (still referenced).");
                updated++;
            }
        }

        if (inserted > 0 || updated > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] InspectionNoteStatuses: Upserted (inserted={inserted}, updated={updated})");
        }

        return (inserted, updated);
    }

    #endregion

    #region Reset Defaults (Explicit User Action)

    /// <summary>
    /// Result of a reset defaults operation.
    /// </summary>
    public class ResetDefaultsResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        
        // TaskTypes
        public int TaskTypesDeleted { get; set; }
        public int TaskTypesInserted { get; set; }
        
        // Statuses
        public int StatusesDeleted { get; set; }
        public int StatusesInserted { get; set; }
        
        // ProjectType-TaskType mappings
        public int TaskTypeMappingsDeleted { get; set; }
        public int TaskTypeMappingsInserted { get; set; }
        
        // ProjectType-Status mappings
        public int StatusMappingsDeleted { get; set; }
        public int StatusMappingsInserted { get; set; }

        public string GetSummary()
        {
            if (!Success)
                return $"Reset failed: {ErrorMessage}";

            var parts = new List<string>();
            
            if (TaskTypesDeleted > 0 || TaskTypesInserted > 0)
                parts.Add($"TaskTypes: -{TaskTypesDeleted}/+{TaskTypesInserted}");
            
            if (StatusesDeleted > 0 || StatusesInserted > 0)
                parts.Add($"Statuses: -{StatusesDeleted}/+{StatusesInserted}");
            
            if (TaskTypeMappingsDeleted > 0 || TaskTypeMappingsInserted > 0)
                parts.Add($"TaskType mappings: -{TaskTypeMappingsDeleted}/+{TaskTypeMappingsInserted}");
            
            if (StatusMappingsDeleted > 0 || StatusMappingsInserted > 0)
                parts.Add($"Status mappings: -{StatusMappingsDeleted}/+{StatusMappingsInserted}");

            return parts.Count > 0 
                ? $"Reset complete: {string.Join(", ", parts)}"
                : "Reset complete: No changes needed";
        }
    }

    /// <summary>
    /// Resets all task management configuration to default values.
    /// This is a DESTRUCTIVE operation that should only be triggered by explicit user action.
    /// 
    /// Performs in a single transaction:
    /// 1. Deletes all existing TaskTypes (except those in use)
    /// 2. Deletes all existing Statuses (except those in use)
    /// 3. Deletes all ProjectType-TaskType mappings
    /// 4. Deletes all ProjectType-Status mappings
    /// 5. Inserts default TaskTypes
    /// 6. Inserts default Statuses
    /// 7. Inserts default ProjectType mappings
    /// 
    /// AUTHORIZATION: Requires full access (IsDomainGroup=1).
    /// </summary>
    public ResetDefaultsResult ResetAllToDefaults()
    {
        var result = new ResetDefaultsResult();
        DevToolsLog.Info("[SeedService] Starting RESET ALL TO DEFAULTS operation...");

        using var transaction = _context.Database.BeginTransaction();
        
        try
        {
            // Step 1: Delete all mappings first (they reference TaskTypes/Statuses)
            result.TaskTypeMappingsDeleted = DeleteAllTaskTypeMappings();
            result.StatusMappingsDeleted = DeleteAllStatusMappings();

            // Step 2: Delete TaskTypes and Statuses (only those not in use by ProjectAssignments)
            result.TaskTypesDeleted = DeleteUnusedTaskTypes();
            result.StatusesDeleted = DeleteUnusedStatuses();

            // Step 3: Insert default TaskTypes
            result.TaskTypesInserted = InsertDefaultTaskTypes();

            // Step 4: Insert default Statuses
            result.StatusesInserted = InsertDefaultStatuses();

            // Step 5: Insert default mappings
            result.TaskTypeMappingsInserted = InsertDefaultTaskTypeMappings();
            result.StatusMappingsInserted = InsertDefaultStatusMappings();

            transaction.Commit();
            result.Success = true;

            DevToolsLog.Info($"[SeedService] RESET ALL TO DEFAULTS completed successfully: {result.GetSummary()}");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            DevToolsLog.Error(ex, "[SeedService] RESET ALL TO DEFAULTS failed - rolled back");
        }

        return result;
    }

    /// <summary>
    /// Resets only the ProjectType mapping rules to defaults.
    /// Preserves TaskTypes and Statuses, only resets which are allowed for each ProjectType.
    /// 
    /// AUTHORIZATION: Requires full access (IsDomainGroup=1).
    /// </summary>
    public ResetDefaultsResult ResetMappingsToDefaults()
    {
        var result = new ResetDefaultsResult();
        DevToolsLog.Info("[SeedService] Starting RESET MAPPINGS TO DEFAULTS operation...");

        using var transaction = _context.Database.BeginTransaction();
        
        try
        {
            // Step 1: Delete all mappings
            result.TaskTypeMappingsDeleted = DeleteAllTaskTypeMappings();
            result.StatusMappingsDeleted = DeleteAllStatusMappings();

            // Step 2: Insert default mappings
            result.TaskTypeMappingsInserted = InsertDefaultTaskTypeMappings();
            result.StatusMappingsInserted = InsertDefaultStatusMappings();

            transaction.Commit();
            result.Success = true;

            DevToolsLog.Info($"[SeedService] RESET MAPPINGS TO DEFAULTS completed: {result.GetSummary()}");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            DevToolsLog.Error(ex, "[SeedService] RESET MAPPINGS TO DEFAULTS failed - rolled back");
        }

        return result;
    }

    #endregion

    #region Delete Operations

    private int DeleteAllTaskTypeMappings()
    {
        var all = _context.ProjectTypeTaskTypes.ToList();
        var count = all.Count;
        
        if (count > 0)
        {
            _context.ProjectTypeTaskTypes.RemoveRange(all);
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Deleted {count} ProjectType-TaskType mappings");
        }
        
        return count;
    }

    private int DeleteAllStatusMappings()
    {
        var all = _context.ProjectTypeStatuses.ToList();
        var count = all.Count;
        
        if (count > 0)
        {
            _context.ProjectTypeStatuses.RemoveRange(all);
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Deleted {count} ProjectType-Status mappings");
        }
        
        return count;
    }

    private int DeleteUnusedTaskTypes()
    {
        // Find TaskTypes that are NOT referenced by any ProjectAssignment
        var usedTaskTypeIds = _context.ProjectAssignments
            .Where(pa => pa.TaskTypeId != null)
            .Select(pa => pa.TaskTypeId!.Value)
            .Distinct()
            .ToHashSet();

        var toDelete = _context.TaskTypes
            .Where(t => !usedTaskTypeIds.Contains(t.Id))
            .ToList();

        var count = toDelete.Count;
        
        if (count > 0)
        {
            _context.TaskTypes.RemoveRange(toDelete);
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Deleted {count} unused TaskTypes (preserved {usedTaskTypeIds.Count} in use)");
        }
        
        return count;
    }

    private int DeleteUnusedStatuses()
    {
        // Find Statuses that are NOT referenced by any ProjectAssignment
        var usedStatusIds = _context.ProjectAssignments
            .Where(pa => pa.StatusId != null)
            .Select(pa => pa.StatusId!.Value)
            .Distinct()
            .ToHashSet();

        var toDelete = _context.ProjectAssignmentStatuses
            .Where(s => !usedStatusIds.Contains(s.Id))
            .ToList();

        var count = toDelete.Count;
        
        if (count > 0)
        {
            _context.ProjectAssignmentStatuses.RemoveRange(toDelete);
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Deleted {count} unused Statuses (preserved {usedStatusIds.Count} in use)");
        }
        
        return count;
    }

    #endregion

    #region Insert Operations

    private int InsertDefaultTaskTypes()
    {
        var definitions = TaskTypeSeedData.Definitions;
        var existingNames = _context.TaskTypes
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;

        foreach (var def in definitions)
        {
            if (!existingNames.Contains(def.Name))
            {
                _context.TaskTypes.Add(new TaskType
                {
                    Name = def.Name,
                    IsActive = def.IsActive,
                    SortOrder = def.SortOrder
                });
                existingNames.Add(def.Name);
                inserted++;
            }
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Inserted {inserted} default TaskTypes");
        }

        return inserted;
    }

    private int InsertDefaultStatuses()
    {
        var definitions = ProjectAssignmentStatusSeedData.Definitions;
        var existing = _context.ProjectAssignmentStatuses
            .Select(s => new { s.Code, s.Name })
            .ToList();
        var existingCodes = existing
            .Where(x => !string.IsNullOrEmpty(x.Code))
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNames = existing.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;

        foreach (var def in definitions)
        {
            if (existingCodes.Contains(def.Code) || existingNames.Contains(def.Name))
                continue;

            _context.ProjectAssignmentStatuses.Add(new ProjectAssignmentStatus
            {
                Code = def.Code,
                Name = def.Name,
                IsOpen = def.IsOpen,
                IsActionable = def.IsActionable,
                SortOrder = def.SortOrder
            });
            existingCodes.Add(def.Code);
            existingNames.Add(def.Name);
            inserted++;
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Inserted {inserted} default Statuses");
        }

        return inserted;
    }

    private int InsertDefaultTaskTypeMappings()
    {
        var mappings = ProjectTypeTaskTypeMappingSeedData.Mappings;
        
        var existingProjectTypeIds = _context.Set<JobType>()
            .Select(j => j.Id)
            .ToHashSet();

        var existingTaskTypeIds = _context.TaskTypes
            .Select(t => t.Id)
            .ToHashSet();

        // Also need to map TaskType names to IDs since we may have just inserted them
        var taskTypeNameToId = _context.TaskTypes
            .ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);

        int inserted = 0;

        foreach (var kvp in mappings)
        {
            var projectTypeId = kvp.Key;
            var taskTypeIds = kvp.Value;

            if (!existingProjectTypeIds.Contains(projectTypeId))
                continue;

            foreach (var taskTypeId in taskTypeIds)
            {
                if (!existingTaskTypeIds.Contains(taskTypeId))
                    continue;

                _context.ProjectTypeTaskTypes.Add(new ProjectTypeTaskType
                {
                    ProjectTypeId = projectTypeId,
                    TaskTypeId = taskTypeId
                });
                inserted++;
            }
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Inserted {inserted} default TaskType mappings");
        }

        return inserted;
    }

    private int InsertDefaultStatusMappings()
    {
        var mappings = ProjectTypeStatusMappingSeedData.Mappings;
        
        var existingProjectTypeIds = _context.Set<JobType>()
            .Select(j => j.Id)
            .ToHashSet();

        var existingStatusIds = _context.ProjectAssignmentStatuses
            .Select(s => s.Id)
            .ToHashSet();

        int inserted = 0;

        foreach (var kvp in mappings)
        {
            var projectTypeId = kvp.Key;
            var statusIds = kvp.Value;

            if (!existingProjectTypeIds.Contains(projectTypeId))
                continue;

            foreach (var statusId in statusIds)
            {
                if (!existingStatusIds.Contains(statusId))
                    continue;

                _context.ProjectTypeStatuses.Add(new ProjectTypeStatus
                {
                    ProjectTypeId = projectTypeId,
                    StatusId = statusId
                });
                inserted++;
            }
        }

        if (inserted > 0)
        {
            _context.SaveChanges();
            DevToolsLog.Info($"[SeedService] Inserted {inserted} default Status mappings");
        }

        return inserted;
    }

    #endregion

    /// <summary>
    /// Resets the seeding flag (for testing purposes only).
    /// </summary>
    public static void ResetSeedingSessionFlag()
    {
        _seedingCompleted = false;
    }

    #region Legacy Compatibility

    /// <summary>
    /// Legacy method - now only seeds static lookups.
    /// Renamed from EnsureSeedData for clarity.
    /// </summary>
    [Obsolete("Use EnsureStaticLookupData instead. This method no longer seeds editable configuration.")]
    public void EnsureSeedData() => EnsureStaticLookupData();

    /// <summary>
    /// Legacy method - now only seeds static lookups.
    /// </summary>
    [Obsolete("Use EnsureStaticLookupDataAsync instead. This method no longer seeds editable configuration.")]
    public Task EnsureSeedDataAsync(CancellationToken cancellationToken = default) 
        => EnsureStaticLookupDataAsync(cancellationToken);

    #endregion
}
