namespace SiNet.Application.Workflow;

/// <summary>
/// A single stage within a workflow definition.
/// </summary>
/// <param name="Id">Stage identifier.</param>
/// <param name="Code">Stable stage code.</param>
/// <param name="Name">Display name.</param>
/// <param name="SortOrder">Ordering position within the definition.</param>
/// <param name="IsInitial">Whether this is the entry stage.</param>
/// <param name="IsFinal">Whether this is a terminal stage.</param>
public sealed record WorkflowStageDefinitionDto(
    int Id,
    string? Code,
    string? Name,
    int SortOrder,
    bool IsInitial,
    bool IsFinal);
