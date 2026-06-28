using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>
/// A workflow template (definition) with its ordered stages.
/// </summary>
/// <param name="Id">Definition identifier.</param>
/// <param name="Code">Stable definition code.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsActive">Whether the definition is currently active/usable.</param>
/// <param name="Stages">Ordered stages belonging to the definition.</param>
public sealed record WorkflowDefinitionDto(
    int Id,
    string? Code,
    string? Name,
    bool IsActive,
    IReadOnlyList<WorkflowStageDefinitionDto> Stages);
