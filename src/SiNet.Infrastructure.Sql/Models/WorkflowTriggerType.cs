namespace SiNetSQL.Models;

/// <summary>
/// Describes what triggered the creation of a <see cref="WorkflowInstance"/>.
/// </summary>
public enum WorkflowTriggerType
{
    /// <summary>Manually started by a user.</summary>
    Manual = 0,

    /// <summary>Started in response to an incoming email.</summary>
    Email = 1,

    /// <summary>Started automatically by the system.</summary>
    System = 2,
}
