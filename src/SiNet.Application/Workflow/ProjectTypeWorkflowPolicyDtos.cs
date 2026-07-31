namespace SiNet.Application.Workflow;

public sealed record ProjectTypeWorkflowPolicySnapshotDto(
    IReadOnlyList<ProjectTypeWorkflowJobTypeDto> JobTypes,
    IReadOnlyList<WorkflowDefinitionOptionDto> WorkflowDefinitions,
    IReadOnlyList<ProjectTypeWorkflowMappingDto> Mappings);

public sealed record ProjectTypeWorkflowJobTypeDto(int Id, string Title);

public sealed record WorkflowDefinitionOptionDto(int Id, string Code, string Name, bool IsActive);

public sealed record ProjectTypeWorkflowMappingDto(
    int Id,
    int ProjectTypeId,
    string ProjectTypeTitle,
    int WorkflowDefinitionId,
    string WorkflowDefinitionCode,
    string WorkflowDefinitionName,
    bool IsDefault,
    bool IsEnabled,
    int SortOrder);

public sealed record ProjectTypeWorkflowWriteResult(bool Success, string? Error)
{
    public static ProjectTypeWorkflowWriteResult Ok() => new(true, null);

    public static ProjectTypeWorkflowWriteResult Fail(string error) =>
        new(false, string.IsNullOrWhiteSpace(error) ? "הפעולה נכשלה." : error);
}

public sealed record ProjectTypeContinuationResult(
    bool Success,
    string? Error,
    IReadOnlyList<int> StartedInstanceIds,
    IReadOnlyList<string> SkippedAlreadyActiveCodes)
{
    public static ProjectTypeContinuationResult Fail(string error) =>
        new(false, error, Array.Empty<int>(), Array.Empty<string>());

    public static ProjectTypeContinuationResult Ok(
        IReadOnlyList<int> started,
        IReadOnlyList<string> skipped) =>
        new(true, null, started, skipped);
}
