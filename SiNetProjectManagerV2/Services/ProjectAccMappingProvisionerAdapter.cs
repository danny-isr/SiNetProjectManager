using SiNet.Application.Projects;
using SiNetSQL.Services.AccBootstrap;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: Application <see cref="IProjectAccMappingProvisioner"/> →
/// AccService / local <see cref="IAccProjectProvisioningService"/>.
/// </summary>
internal sealed class ProjectAccMappingProvisionerAdapter(
    IAccProjectProvisioningService provisioning) : IProjectAccMappingProvisioner
{
    private readonly IAccProjectProvisioningService _provisioning =
        provisioning ?? throw new ArgumentNullException(nameof(provisioning));

    public Task EnsureMappingAsync(int projectId, CancellationToken cancellationToken = default) =>
        _provisioning.EnsureProjectMappingAsync(projectId, cancellationToken);
}
