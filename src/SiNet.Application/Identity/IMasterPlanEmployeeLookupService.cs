namespace SiNet.Application.Identity;

/// <summary>
/// Read-only lookup of MasterPlan employees for native user admin (see
/// <see cref="IMasterPlanEmployeeConnectionProvider"/> for connection configuration).
/// </summary>
public interface IMasterPlanEmployeeLookupService
{
    /// <summary>
    /// Returns employees from all configured MasterPlan databases, merged and de-duplicated by Id
    /// (Replica source wins when the same Id exists in multiple databases).
    /// Includes a leading "no mapping" placeholder row when <paramref name="includeNoMappingOption"/> is true.
    /// </summary>
    Task<IReadOnlyList<MasterPlanEmployeeDto>> GetEmployeesAsync(
        bool includeNoMappingOption = true,
        CancellationToken cancellationToken = default);
}
