namespace SiNet.Infrastructure.Sql.Entities;

/// <summary>
/// Minimal read/write projection of the legacy <c>ActionPermission</c> table for native New System admin.
/// </summary>
public sealed class ActionPermissionEntity
{
    public int Id { get; set; }

    public string ActionCode { get; set; } = string.Empty;

    public string ActionDisplayName { get; set; } = string.Empty;

    public int UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
