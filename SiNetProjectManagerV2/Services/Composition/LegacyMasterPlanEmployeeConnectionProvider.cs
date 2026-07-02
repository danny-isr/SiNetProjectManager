using SiNet.Application.Identity;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Host adapter: binds MasterPlan SQL connection strings from vault/appsettings for native lookup.
/// </summary>
internal sealed class LegacyMasterPlanEmployeeConnectionProvider : IMasterPlanEmployeeConnectionProvider
{
    public MasterPlanEmployeeConnectionSettings GetConnectionSettings()
        => new()
        {
            ReplicaDatabase = AppConfiguration.GetConnectionString("ReplicaDatabase"),
            MasterPlanDatabase = AppConfiguration.GetConnectionString("MasterPlanDatabase"),
        };
}
