namespace SiNet.Application.Projects;

/// <summary>Loads and saves project metadata + job-type/bid lines (number immutable).</summary>
public interface IProjectUpdateService
{
    Task<ProjectEditDto?> GetForEditAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists Draft/Active/Paused workflow instances on the project whose <c>JobTypeId</c>
    /// is currently linked but would be removed if only
    /// <paramref name="remainingJobTypeIds"/> stay selected.
    /// </summary>
    Task<IReadOnlyList<ProjectJobTypeRemovalRiskDto>> GetJobTypeRemovalRiskAsync(
        int projectId,
        IReadOnlyCollection<int> remainingJobTypeIds,
        CancellationToken cancellationToken = default);

    Task<UpdateProjectResult> SaveAsync(
        UpdateProjectCommand command,
        CancellationToken cancellationToken = default);
}
