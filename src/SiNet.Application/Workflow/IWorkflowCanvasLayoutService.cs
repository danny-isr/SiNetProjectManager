using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SiNet.Application.Workflow;

/// <summary>
/// Persists visual canvas layout only (CanvasX/Y). Does not change stage content or transitions.
/// </summary>
public interface IWorkflowCanvasLayoutService
{
    /// <summary>Updates <c>CanvasX</c>/<c>CanvasY</c> for the given stages of one definition.</summary>
    Task SaveStageCanvasPositionsAsync(
        int workflowDefinitionId,
        IReadOnlyList<WorkflowStageCanvasPositionDto> positions,
        CancellationToken cancellationToken = default);
}

/// <summary>One stage's canvas coordinates.</summary>
public sealed record WorkflowStageCanvasPositionDto(int StageId, double CanvasX, double CanvasY);

/// <summary>No-op layout service for design-time / unbound hosts.</summary>
public sealed class NullWorkflowCanvasLayoutService : IWorkflowCanvasLayoutService
{
    public Task SaveStageCanvasPositionsAsync(
        int workflowDefinitionId,
        IReadOnlyList<WorkflowStageCanvasPositionDto> positions,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
