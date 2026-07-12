using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// EF implementation that updates only stage canvas coordinates.
/// </summary>
public sealed class SqlWorkflowCanvasLayoutService : IWorkflowCanvasLayoutService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlWorkflowCanvasLayoutService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    /// <inheritdoc />
    public async Task SaveStageCanvasPositionsAsync(
        int workflowDefinitionId,
        IReadOnlyList<WorkflowStageCanvasPositionDto> positions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var stageIds = positions.Select(p => p.StageId).ToHashSet();
        var stages = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == workflowDefinitionId && stageIds.Contains(s.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stages.Count != stageIds.Count)
        {
            throw new InvalidOperationException(
                $"Some stages do not belong to workflow definition {workflowDefinitionId}.");
        }

        var byId = positions.ToDictionary(p => p.StageId);
        foreach (var stage in stages)
        {
            var pos = byId[stage.Id];
            stage.CanvasX = pos.CanvasX;
            stage.CanvasY = pos.CanvasY;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
