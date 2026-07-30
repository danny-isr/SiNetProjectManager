using SiNet.Application.Projects;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Application port <see cref="IProjectAccMappingProvisioner"/> →
/// <see cref="IAccProjectProvisioningService"/> (Remote AccService or Local in-process).
/// </summary>
internal sealed class ProjectAccMappingProvisionerAdapter(
    IAccProjectProvisioningService provisioning) : IProjectAccMappingProvisioner
{
    private readonly IAccProjectProvisioningService _provisioning =
        provisioning ?? throw new ArgumentNullException(nameof(provisioning));

    public Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default) =>
        _provisioning.EnsureProjectMappingAsync(projectId, cancellationToken);
}
