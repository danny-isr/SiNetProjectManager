namespace SiNet.Application.Projects;

/// <summary>Creates a new project (quote-stage defaults + selected job types).</summary>
public interface IProjectCreateService
{
    Task<decimal> GetNextProjectNumberAsync(CancellationToken cancellationToken = default);

    Task<bool> ProjectNameExistsAsync(string projectName, CancellationToken cancellationToken = default);

    Task<CreateProjectResult> CreateAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional host hook to create on-disk project folders after DB insert.</summary>
public interface IProjectFolderBootstrapper
{
    void CreateFolders(int projectId);
}

/// <summary>
/// Optional host hook to provision <c>ProjectAccMapping</c> (ACC project + folders) after DB insert.
/// Implemented by the host via AccService / <c>IAccProjectProvisioningService</c>.
/// </summary>
public interface IProjectAccMappingProvisioner
{
    Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default);
}
