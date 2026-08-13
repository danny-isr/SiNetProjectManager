using SiNet.Application.Configuration;
using SiNet.Application.Runtime;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Deep probe of the MasterPlan replica connection string in this Windows user's vault.
/// Does not open live <c>Db_Mp_SiEng</c>. Catches missing AD group / SQL login mapping.
/// </summary>
public sealed class MasterPlanReplicaStatusContributor(ISecretVaultStore vault) : ISubsystemStatusContributor
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    public string Key => "masterplan-replica";

    public string DisplayNameHe => "רפליקת MasterPlan (SQL)";

    public SubsystemProbeTier Tier => SubsystemProbeTier.Deep;

    public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = SecretSetupValidators.TestDatabaseFromVault(_vault, SecretCatalog.ReplicaDatabase);

        if (!result.Exists)
        {
            return Task.FromResult(Row(
                SubsystemRuntimeState.NotConfigured,
                "חסר ConnectionString לרפליקה ב-Vault"));
        }

        if (!result.Success)
        {
            return Task.FromResult(Row(
                SubsystemRuntimeState.Degraded,
                result.Detail ?? "אין חיבור לרפליקה — הרשאת SQL חסרה"));
        }

        return Task.FromResult(Row(
            SubsystemRuntimeState.Idle,
            result.Detail is null ? "רפליקה זמינה" : $"רפליקה זמינה — {result.Detail}"));
    }

    private SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
}
