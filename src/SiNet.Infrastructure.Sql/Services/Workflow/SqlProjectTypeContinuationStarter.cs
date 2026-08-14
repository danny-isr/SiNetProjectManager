using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// After QuoteApprovedByClient: require a default enabled mapping per project type,
/// then start one project-bound instance per JobType track (B2).
/// </summary>
public sealed class SqlProjectTypeContinuationStarter(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IWorkflowCommandService workflowCommands,
    IAppLogger? logger = null)
    : IProjectTypeContinuationStarter
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IWorkflowCommandService _workflowCommands =
        workflowCommands ?? throw new ArgumentNullException(nameof(workflowCommands));
    private readonly IAppLogger? _logger = logger;

    public Task<ProjectTypeContinuationResult> ValidateMappingsAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        ResolveAndMaybeStartAsync(projectId, start: false, actingUserId: 0, cancellationToken);

    public Task<ProjectTypeContinuationResult> StartContinuationsAsync(
        int projectId,
        int actingUserId,
        CancellationToken cancellationToken = default) =>
        ResolveAndMaybeStartAsync(projectId, start: true, actingUserId, cancellationToken);

    private async Task<ProjectTypeContinuationResult> ResolveAndMaybeStartAsync(
        int projectId,
        bool start,
        int actingUserId,
        CancellationToken cancellationToken)
    {
        if (projectId <= 0)
            return ProjectTypeContinuationResult.Fail("מזהה פרויקט לא תקין.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var typeIds = await db.TypeOfProjectInProjects.AsNoTracking()
            .Where(tp => tp.ProjectId == projectId && tp.ProjectTypeId != null)
            .Select(tp => tp.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (typeIds.Count == 0)
        {
            return ProjectTypeContinuationResult.Fail(
                "לפרויקט אין סוגי פרויקט — לא ניתן לאשר הצעה ולהפעיל תהליך המשך. הוסף סוג פרויקט לפרויקט.");
        }

        var titleById = await db.JobTypes.AsNoTracking()
            .Where(j => typeIds.Contains(j.Id))
            .Select(j => new { j.Id, j.Title })
            .ToDictionaryAsync(j => j.Id, j => j.Title ?? $"#{j.Id}", cancellationToken)
            .ConfigureAwait(false);

        var mappingRows = await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
            .Where(m =>
                typeIds.Contains(m.ProjectTypeId)
                && m.IsEnabled
                && m.WorkflowDefinition.IsActive)
            .Select(m => new
            {
                m.ProjectTypeId,
                m.WorkflowDefinitionId,
                m.IsDefault,
                m.SortOrder,
                Code = m.WorkflowDefinition.Code,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missingTitles = new List<string>();
        var tracks = new List<(int JobTypeId, int DefinitionId, string? Code)>();

        foreach (var typeId in typeIds)
        {
            var best = mappingRows
                .Where(m => m.ProjectTypeId == typeId)
                .OrderByDescending(m => m.IsDefault)
                .ThenBy(m => m.SortOrder)
                .ThenBy(m => m.WorkflowDefinitionId)
                .FirstOrDefault();

            if (best is null)
            {
                missingTitles.Add(titleById.TryGetValue(typeId, out var t) ? t : $"#{typeId}");
                continue;
            }

            tracks.Add((typeId, best.WorkflowDefinitionId, best.Code));
        }

        if (missingTitles.Count > 0)
        {
            var list = string.Join(", ", missingTitles.Distinct(StringComparer.Ordinal).OrderBy(x => x));
            return ProjectTypeContinuationResult.Fail(
                "חסר מיפוי תהליך לסוג(י) הפרויקט: " + list
                + ". הגדר במנהלה → «מדיניות סוג↔תהליך».");
        }

        tracks = tracks
            .OrderBy(t => t.JobTypeId)
            .ThenBy(t => t.DefinitionId)
            .ToList();

        if (!start)
            return ProjectTypeContinuationResult.Ok(Array.Empty<int>(), Array.Empty<string>());

        var started = new List<int>();
        var skipped = new List<string>();

        foreach (var (jobTypeId, definitionId, code) in tracks)
        {
            var trackLabel = $"{code ?? definitionId.ToString()}/{titleById.GetValueOrDefault(jobTypeId, $"#{jobTypeId}")}";

            var alreadyActive = await db.WorkflowInstances.AsNoTracking()
                .AnyAsync(
                    i => i.ProjectId == projectId
                         && i.IsProjectBound
                         && i.WorkflowDefinitionId == definitionId
                         && i.JobTypeId == jobTypeId
                         && (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused),
                    cancellationToken)
                .ConfigureAwait(false);

            if (alreadyActive)
            {
                skipped.Add(trackLabel);
                continue;
            }

            try
            {
                var result = await _workflowCommands.StartAsync(
                        new StartWorkflowCommand(
                            definitionId,
                            projectId,
                            WorkflowTriggerTypeDto.System,
                            TriggerEntityId: null,
                            UserId: actingUserId,
                            Notes: "post QuoteApprovedByClient",
                            IsProjectBound: true,
                            InitialStageCode: null,
                            JobTypeId: jobTypeId),
                        cancellationToken)
                    .ConfigureAwait(false);

                started.Add(result.Instance.Id);
                Trace.TraceInformation(
                    "[ProjectTypeContinuation] Started definition={0} ({1}) jobType={2} instance={3} project={4}",
                    definitionId,
                    code ?? "?",
                    jobTypeId,
                    result.Instance.Id,
                    projectId);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[ProjectTypeContinuation] outcome=Failed definition={definitionId} jobType={jobTypeId} project={projectId} detail={ex.Message}",
                    ex);
                return ProjectTypeContinuationResult.Fail(
                    $"הפעלת תהליך המשך «{trackLabel}» נכשלה: {ex.Message}");
            }
        }

        return ProjectTypeContinuationResult.Ok(started, skipped);
    }
}
