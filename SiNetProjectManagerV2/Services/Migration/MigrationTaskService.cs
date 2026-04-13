using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services.Migration;

/// <summary>
/// Handles per-row project resolution, configuration pre-check, preview generation,
/// and task creation for the inspection migration workflow.
/// <para>
/// <b>Phase 1 — Preview</b>: Reads each row's project reference, resolves it against the DB,
/// ensures TaskType and statuses exist, and builds a <see cref="MigrationPreviewRow"/> table
/// for user review before any tasks are written.
/// </para>
/// <para>
/// <b>Phase 2 — Commit</b>: For each user-approved preview row, ensures configuration tree
/// links (ProjectTypeTaskType / ProjectTypeStatus) and creates <see cref="ProjectAssignment"/>
/// records directly.
/// </para>
/// </summary>
public sealed class MigrationTaskService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _contextFactory;

    /// <summary>The task type name used for migration-generated tasks.</summary>
    private const string MigrationTaskTypeName = "בדיקת תוכנית";

    public MigrationTaskService(IDbContextFactory<SiNetSQLDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 1: Build Preview (read-only scan + lightweight DB writes
    //           for TaskType & statuses that must exist before preview)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves each row's project in the database, ensures the TaskType and all unique statuses
    /// exist globally, and returns a preview table for user review.
    /// <para>
    /// This method <b>does</b> create the TaskType and status rows if missing (they are global
    /// entities needed to display accurate preview info), but does <b>not</b> create any
    /// ProjectTypeTaskType / ProjectTypeStatus links or ProjectAssignment records yet.
    /// </para>
    /// </summary>
    public async Task<MigrationPreviewResult> BuildPreviewAsync(
        IReadOnlyList<IndexSheetRow> rows,
        IReadOnlyList<string> uniqueStatuses,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        int newStatusesCreated = 0;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // ── Step 1: Ensure TaskType exists globally ──
            var (taskTypeId, taskTypeCreated) = await EnsureTaskTypeGlobalAsync(context, warnings, cancellationToken);

            // ── Step 2: Ensure all statuses exist globally ──
            var statusNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var statusName in uniqueStatuses)
            {
                if (string.IsNullOrWhiteSpace(statusName)) continue;
                var trimmed = statusName.Trim();

                var existing = await context.ProjectAssignmentStatuses
                    .FirstOrDefaultAsync(s => s.Name == trimmed, cancellationToken);

                if (existing != null)
                {
                    statusNameToId[trimmed] = existing.Id;
                }
                else
                {
                    var maxSort = await context.ProjectAssignmentStatuses
                        .MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0;

                    var newStatus = new ProjectAssignmentStatus
                    {
                        Name = trimmed,
                        IsOpen = true,
                        SortOrder = maxSort + 1,
                        DefaultColorHex = "#FFF9C4",
                    };
                    context.ProjectAssignmentStatuses.Add(newStatus);
                    await context.SaveChangesAsync(cancellationToken);

                    statusNameToId[trimmed] = newStatus.Id;
                    newStatusesCreated++;
                    warnings.Add($"Created new status: '{trimmed}' (Id={newStatus.Id})");
                }
            }

            // ── Step 3: Resolve projects and build preview rows ──
            // Cache project lookups to avoid repeated DB hits for the same reference
            var projectCache = new Dictionary<string, (int? Id, string Name, string TypeName)>(StringComparer.OrdinalIgnoreCase);
            var previewRows = new List<MigrationPreviewRow>();

            foreach (var row in rows)
            {
                var projectRef = row.ProjectIdOrName.Trim();
                if (!projectCache.TryGetValue(projectRef, out var resolved))
                {
                    resolved = await ResolveProjectAsync(context, projectRef, cancellationToken);
                    projectCache[projectRef] = resolved;
                }

                var canMigrate = resolved.Id != null && !row.IsApproved;
                var actionParts = new List<string>();

                if (row.IsApproved)
                {
                    actionParts.Add("דילוג — מאושר");
                }
                else if (resolved.Id == null)
                {
                    actionParts.Add("❌ פרויקט לא נמצא — לא ניתן ליצור משימה");
                }
                else
                {
                    actionParts.Add($"יצירת משימה '{MigrationTaskTypeName}'");
                    if (!string.IsNullOrWhiteSpace(row.Status))
                        actionParts.Add($"סטטוס: \"{row.Status}\"");
                }

                previewRows.Add(new MigrationPreviewRow
                {
                    RowIndex = row.RowIndex,
                    SheetProjectRef = projectRef,
                    ResolvedProjectId = resolved.Id,
                    ResolvedProjectName = resolved.Id != null ? resolved.Name : "❌ לא נמצא",
                    ProjectTypeName = resolved.TypeName,
                    ReportNumber = row.ReportNumber,
                    SheetStatus = row.Status,
                    IsApproved = row.IsApproved,
                    ActionDescription = string.Join(" | ", actionParts),
                    CanMigrate = canMigrate,
                    IsSelected = canMigrate,
                });
            }

            return new MigrationPreviewResult
            {
                Rows = previewRows,
                TaskTypeId = taskTypeId,
                TaskTypeCreated = taskTypeCreated,
                TaskTypeName = MigrationTaskTypeName,
                StatusNameToId = statusNameToId,
                NewStatusesCreated = newStatusesCreated,
                Warnings = warnings,
                IsSuccess = true,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MigrationPreviewResult
            {
                IsSuccess = false,
                ErrorMessage = $"Preview build failed: {ex.Message}",
                Warnings = warnings,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Phase 2: Commit — Create tasks for approved preview rows
    //           using Verified Hierarchy (A → B → C → D per row)
    //           Each row uses a FRESH DbContext to avoid tracker poisoning.
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// For each selected preview row, verifies the 3-tier configuration hierarchy
    /// and creates a <see cref="ProjectAssignment"/>. Each row is processed with
    /// its own <see cref="SiNetSQLDbContext"/> so that a failure in one row can
    /// never affect another. All output is sent to <see cref="AppLogger"/>.
    /// </summary>
    public async Task<TaskGenerationResult> CommitTasksAsync(
        IReadOnlyList<MigrationPreviewRow> approvedRows,
        IReadOnlyList<IndexSheetRow> originalRows,
        int taskTypeId,
        IReadOnlyDictionary<string, int> statusNameToId,
        CancellationToken cancellationToken = default)
    {
        const string tag = "[Migration]";
        int created = 0;
        int skipped = 0;
        int duplicates = 0;
        int failed = 0;

        // ── Validate current user (used as fallback assignee) ──
        var fallbackUserId = CurrentUserContext.Instance.CurrentUserId;
        if (fallbackUserId == null)
        {
            AppLogger.Error($"{tag} CurrentUserId is null — cannot create tasks.");
            return new TaskGenerationResult
            {
                IsSuccess = false,
                ErrorMessage = "Current user not resolved. Cannot create tasks.",
            };
        }

        // ── Pre-flight: FK validation + user lookup caches (own context, disposed immediately) ──
        Dictionary<string, int> emailToUserId;
        Dictionary<string, int> nameToUserId;

        await using (var preflight = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var userExists = await preflight.Siusers
                .AnyAsync(u => u.Id == fallbackUserId.Value, cancellationToken);
            if (!userExists)
            {
                AppLogger.Error($"{tag} Fallback UserId={fallbackUserId.Value} does NOT exist in Siusers table.");
                return new TaskGenerationResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"UserId={fallbackUserId.Value} does not exist in Siusers.",
                };
            }

            var taskTypeExists = await preflight.TaskTypes
                .AnyAsync(t => t.Id == taskTypeId, cancellationToken);
            if (!taskTypeExists)
            {
                AppLogger.Error($"{tag} TaskTypeId={taskTypeId} does NOT exist in TaskTypes table.");
                return new TaskGenerationResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"TaskTypeId={taskTypeId} does not exist in TaskTypes.",
                };
            }

            // Build user lookup caches — delegated to the global TaskPriorityEngine
            var userCaches = await TaskPriorityEngine
                .BuildUserLookupCachesAsync(preflight, cancellationToken);
            emailToUserId = userCaches.EmailToUserId;
            nameToUserId = userCaches.NameToUserId;

            AppLogger.Info($"{tag} User cache built — {emailToUserId.Count} emails, {nameToUserId.Count} names.");
        }

        AppLogger.Info($"{tag} Pre-flight OK — FallbackUserId={fallbackUserId.Value}, TaskTypeId={taskTypeId}, Rows={approvedRows.Count}");

        // Cache verified tree links to avoid redundant DB round-trips within this run.
        // The actual links ARE committed to the DB, so fresh contexts will see them.
        var verifiedTaskTypeLinks = new HashSet<int>();
        var verifiedStatusLinks = new HashSet<(int ProjectTypeId, int StatusId)>();

        foreach (var preview in approvedRows)
        {
            var rowLabel = $"Row {preview.RowIndex + 1} | Project='{preview.SheetProjectRef}' | Report='{preview.ReportNumber?.Trim()}'";

            // ── Skip unselected / unresolved ──
            if (!preview.IsSelected)
            {
                skipped++;
                continue;
            }

            if (preview.ResolvedProjectId == null)
            {
                skipped++;
                AppLogger.Info($"{tag} {rowLabel}: Project not resolved — skipping.");
                continue;
            }

            var pId = preview.ResolvedProjectId.Value;

            // ── Fresh context for this row — no tracker contamination ──
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

                // ═══ Step A: Identify Project + ProjectType ═══
                var projectTypeId = await GetProjectTypeIdAsync(context, pId, cancellationToken);
                if (projectTypeId == null)
                {
                    skipped++;
                    AppLogger.Warn($"{tag} {rowLabel}: ProjectId={pId} has no ProjectType — skipping.");
                    continue;
                }

                // ═══ Step B: Ensure TaskType linked to this ProjectType ═══
                if (verifiedTaskTypeLinks.Add(projectTypeId.Value))
                {
                    var ttLinkExists = await context.ProjectTypeTaskTypes
                        .AnyAsync(ptt => ptt.ProjectTypeId == projectTypeId.Value
                            && ptt.TaskTypeId == taskTypeId, cancellationToken);
                    if (!ttLinkExists)
                    {
                        context.ProjectTypeTaskTypes.Add(new ProjectTypeTaskType
                        {
                            ProjectTypeId = projectTypeId.Value,
                            TaskTypeId = taskTypeId,
                        });
                        await context.SaveChangesAsync(cancellationToken);
                        AppLogger.Info($"{tag} Linked TaskType Id={taskTypeId} → ProjectType Id={projectTypeId.Value}");
                    }
                }

                // ═══ Step C: Ensure Status linked to this ProjectType ═══
                var originalRow = originalRows.FirstOrDefault(r => r.RowIndex == preview.RowIndex);
                if (originalRow == null)
                {
                    skipped++;
                    AppLogger.Warn($"{tag} {rowLabel}: Original row data not found — skipping.");
                    continue;
                }

                int? statusId = null;
                if (!string.IsNullOrWhiteSpace(originalRow.Status) &&
                    statusNameToId.TryGetValue(originalRow.Status.Trim(), out var sid))
                {
                    statusId = sid;

                    if (verifiedStatusLinks.Add((projectTypeId.Value, sid)))
                    {
                        var sLinkExists = await context.ProjectTypeStatuses
                            .AnyAsync(pts => pts.ProjectTypeId == projectTypeId.Value
                                && pts.StatusId == sid, cancellationToken);
                        if (!sLinkExists)
                        {
                            context.ProjectTypeStatuses.Add(new ProjectTypeStatus
                            {
                                ProjectTypeId = projectTypeId.Value,
                                StatusId = sid,
                            });
                            await context.SaveChangesAsync(cancellationToken);
                            AppLogger.Info($"{tag} Linked Status Id={sid} ('{originalRow.Status.Trim()}') → ProjectType Id={projectTypeId.Value}");
                        }
                    }
                }

                // ═══ Step D: Build title + explicit duplicate check ═══
                // Title MUST be globally unique (DB constraint ProjectAssignment_TitleIndex
                // is on Title alone), so we embed the ProjectId in the title.
                var reportNum = originalRow.ReportNumber?.Trim() ?? string.Empty;
                var title = !string.IsNullOrWhiteSpace(reportNum)
                    ? $"{MigrationTaskTypeName} {pId} — ביקורת {reportNum}"
                    : $"{MigrationTaskTypeName} {pId} — שורה {originalRow.RowIndex + 1}";

                var alreadyExists = await context.ProjectAssignments
                    .AnyAsync(pa => pa.Title != null && pa.Title == title, cancellationToken);

                if (alreadyExists)
                {
                    duplicates++;
                    AppLogger.Info($"{tag} Duplicate — task already exists: ProjectId={pId}, Title='{title}' — skipping.");
                    continue;
                }

                // ═══ Step E: Resolve assignee (email → name → fallback) via global engine ═══
                var (assigneeId, assigneeSource) = TaskPriorityEngine.ResolveUserFromCaches(
                    originalRow.InspectorEmail, originalRow.InspectorName,
                    emailToUserId, nameToUserId, fallbackUserId.Value);

                if (assigneeSource != "fallback")
                {
                    AppLogger.Info($"{tag} {rowLabel}: Assigned to UserId={assigneeId} (by {assigneeSource})");
                }
                else
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(originalRow.InspectorEmail))
                        parts.Add($"email='{originalRow.InspectorEmail.Trim()}'");
                    if (!string.IsNullOrWhiteSpace(originalRow.InspectorName))
                        parts.Add($"name='{originalRow.InspectorName.Trim()}'");

                    if (parts.Count > 0)
                        AppLogger.Warn($"{tag} {rowLabel}: No user found for {string.Join(", ", parts)} — using fallback UserId={fallbackUserId.Value}");
                }

                // ═══ Step F: Create the ProjectAssignment via global priority engine ═══
                DateTime? inspectionDate = null;
                if (!string.IsNullOrWhiteSpace(originalRow.InspectionDate) &&
                    DateTime.TryParse(originalRow.InspectionDate, out var parsed))
                {
                    inspectionDate = parsed;
                }

                var task = new ProjectAssignment
                {
                    ProjectId = pId,
                    Title = title,
                    TaskTypeId = taskTypeId,
                    StatusId = statusId,
                    AssignedToId = assigneeId,
                    AuthorId = fallbackUserId.Value,
                    Body = BuildTaskBody(originalRow, preview),
                    StartDate = inspectionDate,
                    Created = DateTime.Now,
                    Modified = DateTime.Now,
                };

                // Atomic last-in-queue insertion — assigns Max+1 priority.
                await TaskPriorityEngine.InsertWithAutoPriorityAsync(context, task, cancellationToken);

                created++;
                AppLogger.Info($"{tag} ✅ Created task for {rowLabel} | Title='{title}' | AssignedTo={assigneeId} | Priority={task.WorkPriority}");
            }
            catch (DbUpdateException dbEx)
            {
                failed++;
                var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
                AppLogger.Error($"{tag} DbUpdateException at {rowLabel} | SQL: {innerMsg}");
                AppLogger.Error($"{tag}   ProjectId={pId}, TaskTypeId={taskTypeId}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                AppLogger.Error(ex, $"{tag} Unexpected error at {rowLabel} | ProjectId={pId}");
            }
            // context is disposed here — zero tracker leakage
        }

        AppLogger.Info($"{tag} Complete — Created={created}, Duplicates={duplicates}, Skipped={skipped}, Failed={failed}");

        return new TaskGenerationResult
        {
            TasksCreated = created,
            TasksSkipped = skipped,
            TasksDuplicate = duplicates,
            TasksFailed = failed,
            IsSuccess = true,
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Private helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a project reference (ID or name) from the database.
    /// Tries numeric ID first, then Title, then NameAndNumber.
    /// </summary>
    private static async Task<(int? Id, string Name, string TypeName)> ResolveProjectAsync(
        SiNetSQLDbContext context, string projectRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectRef))
            return (null, string.Empty, string.Empty);

        Project? project = null;

        // Try as numeric ID
        if (int.TryParse(projectRef, out var numericId))
        {
            project = await context.Projects
                .Include(p => p.TypeOfProjectInProjects)
                .FirstOrDefaultAsync(p => p.Id == numericId, ct);
        }

        // Try by Title (exact)
        project ??= await context.Projects
            .Include(p => p.TypeOfProjectInProjects)
            .FirstOrDefaultAsync(p => p.Title != null && p.Title == projectRef, ct);

        // Try by NameAndNumber (exact)
        project ??= await context.Projects
            .Include(p => p.TypeOfProjectInProjects)
            .FirstOrDefaultAsync(p => p.NameAndNumber != null && p.NameAndNumber == projectRef, ct);

        // Try by Title (contains)
        project ??= await context.Projects
            .Include(p => p.TypeOfProjectInProjects)
            .FirstOrDefaultAsync(p => p.Title != null && p.Title.Contains(projectRef), ct);

        if (project == null)
            return (null, string.Empty, string.Empty);

        // Resolve project type name
        var projectTypeLink = project.TypeOfProjectInProjects.FirstOrDefault();
        string typeName = string.Empty;
        if (projectTypeLink?.ProjectTypeId != null)
        {
            var jobType = await context.Set<JobType>()
                .Where(j => j.Id == projectTypeLink.ProjectTypeId.Value)
                .Select(j => j.Title)
                .FirstOrDefaultAsync(ct);
            typeName = jobType ?? string.Empty;
        }

        var displayName = !string.IsNullOrWhiteSpace(project.NameAndNumber)
            ? project.NameAndNumber
            : project.Title ?? $"Project {project.Id}";

        return (project.Id, displayName, typeName);
    }

    /// <summary>Gets the ProjectTypeId for a given project, or null.</summary>
    private static async Task<int?> GetProjectTypeIdAsync(SiNetSQLDbContext context, int projectId, CancellationToken ct)
    {
        return await context.TypeOfProjectInProjects
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.ProjectTypeId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Finds or creates the migration TaskType (globally, without linking to any project type yet).
    /// </summary>
    private static async Task<(int TaskTypeId, bool Created)> EnsureTaskTypeGlobalAsync(
        SiNetSQLDbContext context, List<string> warnings, CancellationToken ct)
    {
        var existing = await context.TaskTypes
            .FirstOrDefaultAsync(t => t.Name == MigrationTaskTypeName, ct);

        if (existing != null)
        {
            warnings.Add($"TaskType '{MigrationTaskTypeName}' already exists (Id={existing.Id}).");
            return (existing.Id, false);
        }

        var maxSort = await context.TaskTypes.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;
        var newType = new TaskType
        {
            Name = MigrationTaskTypeName,
            IsActive = true,
            SortOrder = maxSort + 1,
        };
        context.TaskTypes.Add(newType);
        await context.SaveChangesAsync(ct);

        warnings.Add($"Created TaskType '{MigrationTaskTypeName}' (Id={newType.Id}).");
        return (newType.Id, true);
    }

    /// <summary>
    /// Ensures the TaskType and all statuses are linked to a specific project type
    /// in the configuration tree (ProjectTypeTaskType / ProjectTypeStatus).
    /// </summary>
    private static async Task EnsureTreeLinksAsync(
        SiNetSQLDbContext context, int projectTypeId, int taskTypeId,
        IEnumerable<int> statusIds, List<string> warnings, CancellationToken ct)
    {
        // Link TaskType → ProjectType
        var ttLinkExists = await context.ProjectTypeTaskTypes
            .AnyAsync(ptt => ptt.ProjectTypeId == projectTypeId && ptt.TaskTypeId == taskTypeId, ct);
        if (!ttLinkExists)
        {
            context.ProjectTypeTaskTypes.Add(new ProjectTypeTaskType
            {
                ProjectTypeId = projectTypeId,
                TaskTypeId = taskTypeId,
            });
            warnings.Add($"Linked TaskType '{MigrationTaskTypeName}' → ProjectType Id={projectTypeId}");
        }

        // Link each Status → ProjectType
        foreach (var statusId in statusIds)
        {
            var sLinkExists = await context.ProjectTypeStatuses
                .AnyAsync(pts => pts.ProjectTypeId == projectTypeId && pts.StatusId == statusId, ct);
            if (!sLinkExists)
            {
                context.ProjectTypeStatuses.Add(new ProjectTypeStatus
                {
                    ProjectTypeId = projectTypeId,
                    StatusId = statusId,
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Builds a structured task body from the index sheet row and preview data.
    /// </summary>
    private static string BuildTaskBody(IndexSheetRow row, MigrationPreviewRow preview)
    {
        var parts = new List<string>();

        parts.Add($"פרויקט: {preview.ResolvedProjectName} (Id={preview.ResolvedProjectId})");
        if (!string.IsNullOrWhiteSpace(row.ReportNumber))
            parts.Add($"מספר ביקורת: {row.ReportNumber}");
        if (!string.IsNullOrWhiteSpace(row.InspectionDate))
            parts.Add($"תאריך: {row.InspectionDate}");
        if (!string.IsNullOrWhiteSpace(row.InspectorName))
            parts.Add($"בודק: {row.InspectorName}");
        if (!string.IsNullOrWhiteSpace(row.InspectorEmail))
            parts.Add($"אימייל בודק: {row.InspectorEmail}");
        if (!string.IsNullOrWhiteSpace(row.Status))
            parts.Add($"סטטוס מקורי: {row.Status}");
        if (!string.IsNullOrWhiteSpace(row.ReportLink))
            parts.Add($"קישור: {row.ReportLink}");
        if (!string.IsNullOrWhiteSpace(row.Notes))
            parts.Add($"הערות: {row.Notes}");

        parts.Add($"\n[נוצר אוטומטית מהגירת גיליון אינדקס — שורה {row.RowIndex + 1}]");

        return string.Join("\n", parts);
    }
}
