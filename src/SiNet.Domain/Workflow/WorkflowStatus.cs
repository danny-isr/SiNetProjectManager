namespace SiNet.Domain.Workflow;

/// <summary>
/// Lifecycle status of a workflow instance.
/// <para>
/// Canonical clean-layer definition. The legacy persistence enum
/// (<c>SiNetSQL.Models.WorkflowStatus</c>) currently remains the EF-mapped type;
/// the numeric values here are kept identical so the infrastructure boundary can
/// map between the two with a simple cast. The legacy type is scheduled to be
/// collapsed onto this one during the full workflow DTO migration.
/// </para>
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
