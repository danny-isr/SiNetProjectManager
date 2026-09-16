namespace SiNet.Application.ProjectIntelligence;

/// <summary>
/// Read-only source of Subjects whose ProjectId is already proven by a filing cache
/// (inbox ProjectId + ThreadStatusMapping Assigned, not backfill / AI guess).
/// </summary>
public interface IConfirmedProjectSubjectSource
{
    Task<IReadOnlyList<ConfirmedProjectSubject>> LoadAsync(
        int officeDefaultProjectId,
        CancellationToken cancellationToken = default);
}
