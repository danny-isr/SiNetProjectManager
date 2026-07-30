using SiNet.Application.Abstractions.Autodesk;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Routes project ACC provisioning to Remote AccService or in-process
/// <see cref="AccProjectProvisioningService"/> based on <see cref="IAccServiceModeProvider"/>.
/// </summary>
internal sealed class ModeSwitchingAccProjectProvisioningService(
    IAccServiceModeProvider serviceModeProvider,
    AccProjectProvisioningService localProvisioning,
    RemoteAccProjectProvisioningService remoteProvisioning) : IAccProjectProvisioningService
{
    private readonly IAccServiceModeProvider _serviceModeProvider =
        serviceModeProvider ?? throw new ArgumentNullException(nameof(serviceModeProvider));
    private readonly AccProjectProvisioningService _localProvisioning =
        localProvisioning ?? throw new ArgumentNullException(nameof(localProvisioning));
    private readonly RemoteAccProjectProvisioningService _remoteProvisioning =
        remoteProvisioning ?? throw new ArgumentNullException(nameof(remoteProvisioning));

    private IAccProjectProvisioningService Active =>
        _serviceModeProvider.Mode == AccServiceMode.Remote
            ? _remoteProvisioning
            : _localProvisioning;

    public Task<ProjectAccTargets> EnsureProjectMappingAsync(
        int projectId,
        CancellationToken cancellationToken) =>
        Active.EnsureProjectMappingAsync(projectId, cancellationToken);

    public Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken) =>
        Active.ReconcileProjectMembersAsync(accProjectId, cancellationToken);

    public Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken) =>
        Active.ReconcileAllProjectsAsync(cancellationToken);

    public Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId,
        string accFolderId,
        int? siProjectId,
        CancellationToken cancellationToken) =>
        Active.EnsureCustomAttributeDefinitionsAsync(
            accProjectId,
            accFolderId,
            siProjectId,
            cancellationToken);

    public Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(
        CancellationToken cancellationToken) =>
        Active.ListAvailableTemplatesAsync(cancellationToken);

    public Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken) =>
        Active.ProbeFolderPermissionsAsync(cancellationToken);

    public Task<string> ProbeFolderPermissionsFromTemplateAsync(
        string templateName,
        CancellationToken cancellationToken) =>
        Active.ProbeFolderPermissionsFromTemplateAsync(templateName, cancellationToken);
}
