using System.Collections.Generic;
using System.Linq;
using SiNet.Application.Workflow;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Central entity → DTO mapping for the workflow read slice.
/// Converts EF-mapped <c>SiNetSQL.Models</c> workflow entities into the clean
/// <see cref="SiNet.Application.Workflow"/> DTOs exposed by the ports.
/// <para>
/// Navigation properties are mapped only when loaded (null-conditional), so callers
/// control fetch depth via their EF <c>Include</c> chains. Workflow status is translated
/// through <see cref="WorkflowStatusMappings.ToDomain(WorkflowStatus)"/>.
/// </para>
/// </summary>
internal static class WorkflowDtoMappings
{
    public static WorkflowUserRefDto ToDto(this Siuser user) =>
        new(user.Id, user.Name);

    public static WorkflowProjectRefDto ToDto(this Project project) =>
        new(project.Id, project.Number, project.Title);

    public static WorkflowStageDefinitionDto ToDto(this WorkflowStageDefinition stage) =>
        new(stage.Id, stage.Code, stage.Name, stage.SortOrder, stage.IsInitial, stage.IsFinal);

    public static WorkflowDefinitionDto ToDto(this WorkflowDefinition definition) =>
        new(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.IsActive,
            definition.Stages is { Count: > 0 }
                ? definition.Stages.OrderBy(s => s.SortOrder).Select(s => s.ToDto()).ToList()
                : []);

    public static WorkflowStageTransitionDto ToDto(this WorkflowStageTransition transition) =>
        new(
            transition.Id,
            transition.FromStageId,
            transition.ToStageId,
            transition.ToStage?.ToDto(),
            transition.TransitionedByUser?.ToDto(),
            transition.TransitionedAtUtc,
            transition.Notes);

    public static WorkflowInstanceDto ToDto(this WorkflowInstance instance) =>
        new(
            instance.Id,
            instance.WorkflowDefinitionId,
            instance.ProjectId,
            instance.Status.ToDomain(),
            instance.CurrentStageId,
            instance.CreatedAtUtc,
            instance.CompletedAtUtc,
            instance.Notes,
            instance.WorkflowDefinition?.ToDto(),
            instance.CurrentStage?.ToDto(),
            instance.Project?.ToDto(),
            instance.CreatedByUser?.ToDto(),
            instance.StageTransitions is { Count: > 0 }
                ? instance.StageTransitions
                    .OrderBy(t => t.TransitionedAtUtc)
                    .Select(t => t.ToDto())
                    .ToList()
                : []);

    public static List<WorkflowDefinitionDto> ToDtoList(this IEnumerable<WorkflowDefinition> definitions) =>
        definitions.Select(d => d.ToDto()).ToList();

    public static List<WorkflowInstanceDto> ToDtoList(this IEnumerable<WorkflowInstance> instances) =>
        instances.Select(i => i.ToDto()).ToList();

    public static List<WorkflowStageDefinitionDto> ToDtoList(this IEnumerable<WorkflowStageDefinition> stages) =>
        stages.Select(s => s.ToDto()).ToList();
}
