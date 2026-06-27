namespace SiNetSQL.Models;

/// <summary>
/// Per-user color override for a task status.
/// When present, this color takes priority over the global DefaultColorHex
/// on <see cref="ProjectAssignmentStatus"/>.
/// </summary>
public class UserStatusPreference
{
    public int Id { get; set; }

    public int SiuserId { get; set; }

    public int StatusId { get; set; }

    /// <summary>
    /// User-chosen color override (hex, e.g. "#42A5F5").
    /// </summary>
    public string OverrideColorHex { get; set; } = null!;

    // Navigation properties
    public virtual Siuser Siuser { get; set; } = null!;
    public virtual ProjectAssignmentStatus Status { get; set; } = null!;
}
