using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>SQL admin CRUD for <see cref="ProjectTypeWorkflowDefinition"/> mappings.</summary>
public sealed class SqlProjectTypeWorkflowPolicyAdminService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IProjectTypeWorkflowPolicyAdminService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<ProjectTypeWorkflowPolicySnapshotDto> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var jobTypes = await db.JobTypes.AsNoTracking()
            .Where(j => j.Title != null && j.Title != "")
            .OrderBy(j => j.Title)
            .Select(j => new ProjectTypeWorkflowJobTypeDto(j.Id, j.Title!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var definitions = await db.WorkflowDefinitions.AsNoTracking()
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.Name)
            .Select(d => new WorkflowDefinitionOptionDto(
                d.Id,
                d.Code ?? string.Empty,
                d.Name ?? string.Empty,
                d.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mappings = await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
            .OrderBy(m => m.ProjectType.Title)
            .ThenByDescending(m => m.IsDefault)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.WorkflowDefinition.Name)
            .Select(m => new ProjectTypeWorkflowMappingDto(
                m.Id,
                m.ProjectTypeId,
                m.ProjectType.Title ?? string.Empty,
                m.WorkflowDefinitionId,
                m.WorkflowDefinition.Code ?? string.Empty,
                m.WorkflowDefinition.Name ?? string.Empty,
                m.IsDefault,
                m.IsEnabled,
                m.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProjectTypeWorkflowPolicySnapshotDto(jobTypes, definitions, mappings);
    }

    public async Task<ProjectTypeWorkflowWriteResult> UpsertMappingAsync(
        int projectTypeId,
        int workflowDefinitionId,
        bool isDefault,
        bool isEnabled,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (projectTypeId <= 0)
            return ProjectTypeWorkflowWriteResult.Fail("סוג פרויקט לא תקין.");
        if (workflowDefinitionId <= 0)
            return ProjectTypeWorkflowWriteResult.Fail("הגדרת תהליך לא תקינה.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var jobTypeExists = await db.JobTypes.AsNoTracking()
            .AnyAsync(j => j.Id == projectTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!jobTypeExists)
            return ProjectTypeWorkflowWriteResult.Fail("סוג הפרויקט לא נמצא.");

        var definition = await db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return ProjectTypeWorkflowWriteResult.Fail("הגדרת התהליך לא נמצאה.");
        if (!definition.IsActive)
            return ProjectTypeWorkflowWriteResult.Fail("לא ניתן לשייך הגדרת תהליך שאינה פעילה.");

        var existing = await db.ProjectTypeWorkflowDefinitions
            .FirstOrDefaultAsync(
                m => m.ProjectTypeId == projectTypeId && m.WorkflowDefinitionId == workflowDefinitionId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new ProjectTypeWorkflowDefinition
            {
                ProjectTypeId = projectTypeId,
                WorkflowDefinitionId = workflowDefinitionId,
            };
            db.ProjectTypeWorkflowDefinitions.Add(existing);
        }

        existing.IsEnabled = isEnabled;
        existing.SortOrder = sortOrder;

        if (isDefault)
        {
            await ClearDefaultsForTypeAsync(db, projectTypeId, cancellationToken).ConfigureAwait(false);
            existing.IsDefault = true;
        }
        else
        {
            existing.IsDefault = false;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProjectTypeWorkflowWriteResult.Ok();
    }

    public async Task<ProjectTypeWorkflowWriteResult> SetEnabledAsync(
        int mappingId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mapping = await db.ProjectTypeWorkflowDefinitions
            .FirstOrDefaultAsync(m => m.Id == mappingId, cancellationToken)
            .ConfigureAwait(false);
        if (mapping is null)
            return ProjectTypeWorkflowWriteResult.Fail("המיפוי לא נמצא.");

        mapping.IsEnabled = isEnabled;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProjectTypeWorkflowWriteResult.Ok();
    }

    public async Task<ProjectTypeWorkflowWriteResult> SetDefaultAsync(
        int mappingId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mapping = await db.ProjectTypeWorkflowDefinitions
            .FirstOrDefaultAsync(m => m.Id == mappingId, cancellationToken)
            .ConfigureAwait(false);
        if (mapping is null)
            return ProjectTypeWorkflowWriteResult.Fail("המיפוי לא נמצא.");

        await ClearDefaultsForTypeAsync(db, mapping.ProjectTypeId, cancellationToken).ConfigureAwait(false);
        mapping.IsDefault = true;
        mapping.IsEnabled = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProjectTypeWorkflowWriteResult.Ok();
    }

    public async Task<ProjectTypeWorkflowWriteResult> DeleteMappingAsync(
        int mappingId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mapping = await db.ProjectTypeWorkflowDefinitions
            .FirstOrDefaultAsync(m => m.Id == mappingId, cancellationToken)
            .ConfigureAwait(false);
        if (mapping is null)
            return ProjectTypeWorkflowWriteResult.Fail("המיפוי לא נמצא.");

        db.ProjectTypeWorkflowDefinitions.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProjectTypeWorkflowWriteResult.Ok();
    }

    private static async Task ClearDefaultsForTypeAsync(
        SiNetSQLDbContext db,
        int projectTypeId,
        CancellationToken cancellationToken)
    {
        var defaults = await db.ProjectTypeWorkflowDefinitions
            .Where(m => m.ProjectTypeId == projectTypeId && m.IsDefault)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in defaults)
            row.IsDefault = false;
    }
}
