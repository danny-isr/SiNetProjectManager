using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.Workflow;

/// <summary>
/// Resolves which <see cref="WorkflowDefinition"/>s are allowed for a given project
/// based on the project's <see cref="JobType"/> (ProjectType) mappings.
/// <para>
/// A project's allowed workflows = UNION of all its ProjectType→WorkflowDefinition mappings.
/// Only enabled mappings with active definitions are returned.
/// </para>
/// </summary>
public class ProjectWorkflowPolicyService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IProjectWorkflowPolicyService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;

    /// <summary>
    /// Returns allowed workflow definitions for a project, resolved via its ProjectTypes.
    /// Results are ordered by <see cref="ProjectTypeWorkflowDefinition.SortOrder"/> then by name.
    /// </summary>
    public async ValueTask<List<WorkflowDefinitionDto>> GetAllowedWorkflowsAsync(
        int projectId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get the project's ProjectType IDs
        var projectTypeIds = await db.TypeOfProjectInProjects
            .AsNoTracking()
            .Where(tp => tp.ProjectId == projectId && tp.ProjectTypeId != null)
            .Select(tp => tp.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (projectTypeIds.Count == 0)
            return [];

        var definitions = await GetAllowedWorkflowsForProjectTypesAsync(db, projectTypeIds, ct);
        return definitions.ToDtoList();
    }

    /// <summary>
    /// Returns allowed workflow definitions for a set of ProjectType IDs.
    /// </summary>
    public async ValueTask<List<WorkflowDefinitionDto>> GetAllowedWorkflowsForProjectTypesAsync(
        IReadOnlyList<int> projectTypeIds, CancellationToken ct)
    {
        if (projectTypeIds.Count == 0)
            return [];

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var definitions = await GetAllowedWorkflowsForProjectTypesAsync(db, projectTypeIds, ct);
        return definitions.ToDtoList();
    }

    /// <summary>
    /// Checks whether a specific workflow definition is allowed for a project.
    /// Returns <c>true</c> if no mappings exist (open policy) or if a mapping allows it.
    /// </summary>
    public async ValueTask<bool> IsWorkflowAllowedAsync(
        int projectId, int workflowDefinitionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var projectTypeIds = await db.TypeOfProjectInProjects
            .AsNoTracking()
            .Where(tp => tp.ProjectId == projectId && tp.ProjectTypeId != null)
            .Select(tp => tp.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // If the project has no project types, allow all (open policy)
        if (projectTypeIds.Count == 0)
            return true;

        // Check if any mapping exists at all for these project types
        var anyMappingExists = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(m => projectTypeIds.Contains(m.ProjectTypeId) && m.IsEnabled, ct);

        // If no mappings are configured yet, allow all (open policy)
        if (!anyMappingExists)
            return true;

        // Check if the specific workflow is allowed
        return await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(m =>
                projectTypeIds.Contains(m.ProjectTypeId) &&
                m.WorkflowDefinitionId == workflowDefinitionId &&
                m.IsEnabled, ct);
    }

    private static async Task<List<WorkflowDefinition>> GetAllowedWorkflowsForProjectTypesAsync(
        SiNetSQLDbContext db, IReadOnlyList<int> projectTypeIds, CancellationToken ct)
    {
        // Check if any mappings exist for these project types
        var anyMappingExists = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(m => projectTypeIds.Contains(m.ProjectTypeId) && m.IsEnabled, ct);

        // If no mappings are configured, return all active definitions (open policy)
        if (!anyMappingExists)
        {
            return await db.WorkflowDefinitions
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .ToListAsync(ct);
        }

        // UNION of all allowed workflows across the project types.
        // Ordering precedence: IsDefault DESC, then SortOrder, then Name.
        // A workflow can be mapped via multiple ProjectTypes; we keep the
        // single best mapping per workflow (by the same precedence) so the
        // final ordering is stable and the default workflow comes first.
        var rows = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .Where(m =>
                projectTypeIds.Contains(m.ProjectTypeId) &&
                m.IsEnabled &&
                m.WorkflowDefinition.IsActive)
            .Select(m => new
            {
                m.IsDefault,
                m.SortOrder,
                Definition = m.WorkflowDefinition,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Definition.Id)
            .Select(g => g
                .OrderByDescending(r => r.IsDefault)
                .ThenBy(r => r.SortOrder)
                .ThenBy(r => r.Definition.Name)
                .First())
            .OrderByDescending(r => r.IsDefault)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Definition.Name)
            .Select(r => r.Definition)
            .ToList();
    }
}
