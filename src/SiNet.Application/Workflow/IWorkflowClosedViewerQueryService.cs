using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SiNet.Application.Workflow;

/// <summary>
/// Read-only port for the closed-world workflow definition viewer.
/// Returns full definition graphs and closed catalogs; never mutates persistence.
/// </summary>
public interface IWorkflowClosedViewerQueryService
{
    /// <summary>Loads all workflow definitions with stages, transitions, actions, and stage-tasks.</summary>
    Task<IReadOnlyList<WorkflowDefinitionGraphDto>> GetDefinitionGraphsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns the closed catalogs used for orphan checks and dry-run selectors.</summary>
    Task<WorkflowClosedWorldCatalogDto> GetCatalogsAsync(
        CancellationToken cancellationToken = default);
}
