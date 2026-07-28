using SiNet.Application.Configuration;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// MasterPlan employee lookup connection strings from the Credential Vault.
/// </summary>
public sealed class VaultMasterPlanEmployeeConnectionProvider(ISecretVaultStore vault)
    : IMasterPlanEmployeeConnectionProvider
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    public MasterPlanEmployeeConnectionSettings GetConnectionSettings()
        => new()
        {
            ReplicaDatabase = _vault.GetSecret(SecretCatalog.ReplicaDatabase),
            MasterPlanDatabase = _vault.GetSecret(SecretCatalog.MasterPlanDatabase),
        };
}
