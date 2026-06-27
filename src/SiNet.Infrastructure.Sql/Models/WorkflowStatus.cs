namespace SiNetSQL.Models;

/// <summary>
/// Lifecycle status of a <see cref="WorkflowInstance"/>.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>Instance created but not yet started.</summary>
    Draft = 0,

    /// <summary>Actively progressing through stages.</summary>
    Active = 1,

    /// <summary>Temporarily paused by a user.</summary>
    Paused = 2,

    /// <summary>All stages completed successfully.</summary>
    Completed = 3,

    /// <summary>Cancelled before completion.</summary>
    Cancelled = 4,
}
