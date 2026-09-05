using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>Shared helpers for foundation workflow-transition action handlers in Infrastructure.Sql.</summary>
internal static class WorkflowActionHelpers
{
    /// <summary>
    /// Well-known <see cref="ActionExecutionCommand.Data"/> key carrying a live, caller-owned
    /// <see cref="SiNetSQLDbContext"/> so DB-writing transition handlers can enlist in the atomic
    /// task-close + auto-advance transaction instead of opening their own context. Infra-only:
    /// the value is an infrastructure type, so this key never appears in the Application catalog.
    /// </summary>
    internal const string AmbientDbContextKey = "__AmbientDbContext";

    /// <summary>Returns the ambient shared <see cref="SiNetSQLDbContext"/> if the caller supplied one.</summary>
    internal static SiNetSQLDbContext? TryGetAmbientDbContext(ActionExecutionCommand command)
    {
        if (command.Data is not null
            && command.Data.TryGetValue(AmbientDbContextKey, out var raw)
            && raw is SiNetSQLDbContext db)
        {
            return db;
        }

        return null;
    }

    /// <summary>
    /// Resolves the <see cref="SiNetSQLDbContext"/> a handler should use: the ambient shared context
    /// when present (so the handler enlists in the caller's atomic transaction), otherwise a fresh one
    /// from the factory. <paramref name="owns"/> indicates whether the caller must dispose it.
    /// </summary>
    internal static async Task<(SiNetSQLDbContext Db, bool Owns)> ResolveDbContextAsync(
        ActionExecutionCommand command,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        CancellationToken ct)
    {
        var ambient = TryGetAmbientDbContext(command);
        if (ambient is not null)
            return (ambient, false);

        var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return (db, true);
    }

    internal static string BuildStageTag(int stageId) => $"Stage:{stageId}";

    internal static string? ReadDataString(ActionExecutionCommand command, string key)
    {
        if (command.Data is not null && command.Data.TryGetValue(key, out var raw) && raw is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        return null;
    }

    internal static int? ReadDataInt(ActionExecutionCommand command, string key)
    {
        if (command.Data is null || !command.Data.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    internal static string? ReadConfigString(ActionExecutionCommand command, string propertyName)
    {
        var configJson = ReadDataString(command, ActionExecutionDataKeys.ConfigJson);
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return null;
    }

    /// <summary>
    /// Reads a list of strings from the transition <c>ConfigJson</c> property <paramref name="propertyName"/>.
    /// Accepts either a JSON array of strings (<c>["a","b"]</c>) or a single delimited string
    /// (<c>"a, b; c"</c>, split on comma/semicolon). Returns an empty list when absent or malformed.
    /// </summary>
    internal static IReadOnlyList<string> ReadConfigStringList(ActionExecutionCommand command, string propertyName)
    {
        var configJson = ReadDataString(command, ActionExecutionDataKeys.ConfigJson);
        if (string.IsNullOrWhiteSpace(configJson))
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(propertyName, out var prop))
            {
                return Array.Empty<string>();
            }

            if (prop.ValueKind == JsonValueKind.String)
                return SplitDelimited(prop.GetString());

            if (prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var element in prop.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var value = element.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            list.Add(value.Trim());
                    }
                }

                return list;
            }
        }
        catch (JsonException)
        {
            // fall through to empty
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> SplitDelimited(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split(
            new[] { ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static async Task<(bool Success, string Message)> SetProjectStatusByCodeAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string statusCode,
        int workflowInstanceId,
        int userId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await SetProjectStatusByCodeAsync(db, statusCode, workflowInstanceId, userId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared-context overload: sets the project status against the caller-provided <paramref name="db"/>
    /// so the write enlists in the caller's transaction.
    /// </summary>
    internal static async Task<(bool Success, string Message)> SetProjectStatusByCodeAsync(
        SiNetSQLDbContext db,
        string statusCode,
        int workflowInstanceId,
        int userId,
        CancellationToken ct)
    {
        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.Id == workflowInstanceId)
            .Select(i => new { i.ProjectId, i.IsProjectBound })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (instance is null)
            return (false, $"Workflow instance {workflowInstanceId} not found.");

        // Proposal / price-quote instances keep an office ProjectId for FK integrity only.
        // Mutating that project's status would pollute "ניהול משרד" — skip without failing the transition.
        if (!instance.IsProjectBound)
            return (true, $"Skipped SetProjectStatus '{statusCode}': workflow instance {workflowInstanceId} is not project-bound.");

        var status = await db.ProjectStatuses
            .FirstOrDefaultAsync(s => s.Code == statusCode && s.IsActive, ct)
            .ConfigureAwait(false);

        if (status is null)
            return (false, $"Project status '{statusCode}' is not seeded.");

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == instance.ProjectId, ct).ConfigureAwait(false);
        if (project is null)
            return (false, $"Project {instance.ProjectId} not found.");

        if (project.ProjectStatusId != status.Id)
        {
            project.ProjectStatusId = status.Id;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        _ = userId;
        return (true, $"Project status updated to '{statusCode}'.");
    }

    /// <summary>
    /// System-closes the given tasks (sets them to <see cref="TaskStatusCodes.Completed"/>, clears the
    /// work-queue priority, records a StatusChange event) without routing through task-completion policy
    /// or requesting workflow auto-advance. Used by the ClosePreviousStageTasks and CloseProject
    /// transition handlers, mirroring the legacy coordinator's <c>CloseTasksAsSystemAsync</c> intent.
    /// Already-closed tasks are skipped. Returns the number of tasks actually closed.
    /// </summary>
    internal static async Task<(bool Success, int ClosedCount, string? Error)> CloseTasksAsSystemAsync(
        SiNetSQLDbContext db,
        IReadOnlyCollection<int> taskIds,
        int userId,
        string note,
        CancellationToken ct)
    {
        if (taskIds.Count == 0)
            return (true, 0, null);

        var completedStatus = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Completed, ct)
            .ConfigureAwait(false);

        if (completedStatus is null)
            return (false, 0, $"Task status '{TaskStatusCodes.Completed}' is not configured.");

        var tasks = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Where(t => taskIds.Contains(t.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var nowUtc = DateTime.UtcNow;
        var closedCount = 0;

        foreach (var task in tasks)
        {
            if (task.AssignmentStatus is not null && !task.AssignmentStatus.IsOpen)
                continue;

            var oldStatusId = task.StatusId;
            task.StatusId = completedStatus.Id;
            task.Status = completedStatus.Code;
            // Caller owns ambient DbContext / transaction — do not SaveChanges here.
            await TaskQueuePriorityEngine.RemoveFromQueueAsync(
                    db, task, compact: true, saveChanges: false, cancellationToken: ct)
                .ConfigureAwait(false);
            task.Modified = DateTime.Now;
            task.EditorId = userId;

            db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
            {
                ProjectAssignmentId = task.Id,
                EventType = "StatusChange",
                OldStatusId = oldStatusId,
                NewStatusId = completedStatus.Id,
                Note = note,
                CreatedByUserId = userId,
                CreatedDate = nowUtc,
            });

            closedCount++;
        }

        if (closedCount > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (true, closedCount, null);
    }
}
