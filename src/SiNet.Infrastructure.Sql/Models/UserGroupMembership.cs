namespace SiNetSQL.Models;

/// <summary>
/// Join entity linking <see cref="Siuser"/> to <see cref="UserGroup"/>.
/// A user can belong to multiple groups; a group can have multiple users.
/// </summary>
public class UserGroupMembership
{
    public int Id { get; set; }

    public int SiuserId { get; set; }
    public virtual Siuser Siuser { get; set; } = null!;

    public int UserGroupId { get; set; }
    public virtual UserGroup UserGroup { get; set; } = null!;
}
