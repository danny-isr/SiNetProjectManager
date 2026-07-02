namespace SiNet.Infrastructure.Sql.Entities;

/// <summary>
/// Minimal read/write projection of the legacy <c>SIUser</c> table for native New System user admin.
/// </summary>
public sealed class SiUserEntity
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? LoginName { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public bool? IsDomainGroup { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Stored as <see cref="SiNet.Application.Identity.AppRole"/> numeric value.</summary>
    public int Role { get; set; } = 1;

    /// <summary>Stored as <see cref="SiNet.Application.Identity.AppAccUserType"/> numeric value.</summary>
    public int AccUserType { get; set; }

    public int? MasterPlanEmployeeId { get; set; }
}
