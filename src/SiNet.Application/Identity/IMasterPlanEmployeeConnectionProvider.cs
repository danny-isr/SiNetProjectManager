namespace SiNet.Application.Identity;

/// <summary>
/// Supplies MasterPlan database connection strings to
/// <see cref="IMasterPlanEmployeeLookupService"/>. Host binds this to vault/appsettings —
/// Infrastructure.Sql never hardcodes connection strings.
/// </summary>
public interface IMasterPlanEmployeeConnectionProvider
{
    /// <summary>Returns configured connection strings (either or both may be null/empty).</summary>
    MasterPlanEmployeeConnectionSettings GetConnectionSettings();
}

/// <summary>
/// Two optional MasterPlan SQL sources. Keys match legacy vault names
/// (<c>ReplicaDatabase</c>, <c>MasterPlanDatabase</c>).
/// </summary>
public sealed class MasterPlanEmployeeConnectionSettings
{
    /// <summary>Replica DB (legacy <c>ReplicaDatabase</c>) — primary employee Id source for SIUser mapping.</summary>
    public string? ReplicaDatabase { get; init; }

    /// <summary>Native MasterPlan DB (legacy <c>MasterPlanDatabase</c>) — <c>dbo.Employees</c>.</summary>
    public string? MasterPlanDatabase { get; init; }
}

/// <summary>Default provider when the host has not bound MasterPlan connections yet.</summary>
public sealed class NullMasterPlanEmployeeConnectionProvider : IMasterPlanEmployeeConnectionProvider
{
    public static NullMasterPlanEmployeeConnectionProvider Instance { get; } = new();

    public MasterPlanEmployeeConnectionSettings GetConnectionSettings() => new();
}
