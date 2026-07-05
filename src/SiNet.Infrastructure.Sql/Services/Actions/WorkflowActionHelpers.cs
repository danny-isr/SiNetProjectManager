using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>Shared helpers for foundation workflow-transition action handlers in Infrastructure.Sql.</summary>
internal static class WorkflowActionHelpers
{
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

    internal static async Task<(bool Success, string Message)> SetProjectStatusByCodeAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string statusCode,
        int workflowInstanceId,
        int userId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var projectId = await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.Id == workflowInstanceId && i.IsProjectBound)
            .Select(i => (int?)i.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (projectId is null)
            return (false, $"Workflow instance {workflowInstanceId} is not project-bound.");

        var status = await db.ProjectStatuses
            .FirstOrDefaultAsync(s => s.Code == statusCode && s.IsActive, ct)
            .ConfigureAwait(false);

        if (status is null)
            return (false, $"Project status '{statusCode}' is not seeded.");

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId.Value, ct).ConfigureAwait(false);
        if (project is null)
            return (false, $"Project {projectId.Value} not found.");

        if (project.ProjectStatusId != status.Id)
        {
            project.ProjectStatusId = status.Id;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        _ = userId;
        return (true, $"Project status updated to '{statusCode}'.");
    }
}
