namespace SiNet.Application.Workflow;

/// <summary>Admin CRUD for <c>ProjectTypeWorkflowDefinition</c> mappings (JobType ↔ Workflow).</summary>
public interface IProjectTypeWorkflowPolicyAdminService
{
    Task<ProjectTypeWorkflowPolicySnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<ProjectTypeWorkflowWriteResult> UpsertMappingAsync(
        int projectTypeId,
        int workflowDefinitionId,
        bool isDefault,
        bool isEnabled,
        int sortOrder,
        CancellationToken cancellationToken = default);

    Task<ProjectTypeWorkflowWriteResult> SetEnabledAsync(
        int mappingId,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<ProjectTypeWorkflowWriteResult> SetDefaultAsync(
        int mappingId,
        CancellationToken cancellationToken = default);

    Task<ProjectTypeWorkflowWriteResult> DeleteMappingAsync(
        int mappingId,
        CancellationToken cancellationToken = default);
}
