using Microsoft.EntityFrameworkCore;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Enforces the admin cap on open (Active/Paused) child workflow instances per
/// project + definition. Setting: <see cref="WorkflowSystemSettingsDto.MaxOpenChildInstances"/>
/// (default <see cref="SystemSettingsDefaults.WorkflowMaxOpenChildInstances"/>).
/// </summary>
internal static class WorkflowOpenChildInstanceCap
{
    public static async Task<int> ResolveMaxAsync(
        ISystemSettingsQueryService? systemSettings,
        CancellationToken cancellationToken)
    {
        if (systemSettings is null)
            return SystemSettingsDefaults.WorkflowMaxOpenChildInstances;

        var settings = await systemSettings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return Math.Max(1, settings.Workflow.MaxOpenChildInstances);
    }

    public static async Task<(bool Allowed, int OpenCount, int Max, string? BlockMessageHebrew)> TryAllowStartAsync(
        SiNetSQLDbContext db,
        int projectId,
        int childDefinitionId,
        int maxOpen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        maxOpen = Math.Max(1, maxOpen);

        var openCount = await db.WorkflowInstances
            .AsNoTracking()
            .CountAsync(
                i => i.ProjectId == projectId
                     && i.WorkflowDefinitionId == childDefinitionId
                     && (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused),
                cancellationToken)
            .ConfigureAwait(false);

        if (openCount >= maxOpen)
        {
            var message =
                $"לא ניתן לפתוח תת-תהליך נוסף: כבר יש {openCount} מופעים פתוחים (מכסה {maxOpen}). " +
                "יש להכריע או לסיים מופע קיים לפני פתיחת חדש.";
            return (false, openCount, maxOpen, message);
        }

        return (true, openCount, maxOpen, null);
    }
}
