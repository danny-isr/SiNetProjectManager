using LegacyWorkflowStatus = SiNetSQL.Models.WorkflowStatus;
using DomainWorkflowStatus = SiNet.Domain.Workflow.WorkflowStatus;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Boundary mapping between the legacy EF-mapped <see cref="LegacyWorkflowStatus"/>
/// (<c>SiNetSQL.Models.WorkflowStatus</c>) and the canonical clean-layer
/// <see cref="DomainWorkflowStatus"/> (<c>SiNet.Domain.Workflow.WorkflowStatus</c>).
/// <para>
/// Both enums intentionally share identical numeric values, so the mapping is a
/// checked cast. These helpers are the single place to translate workflow status at
/// the infrastructure boundary and will be consumed by the workflow ports once they
/// move to clean DTOs (full migration). They are additive and not yet wired into any
/// port, so existing behavior is unchanged.
/// </para>
/// </summary>
public static class WorkflowStatusMappings
{
    /// <summary>Maps a legacy EF status to the canonical domain status.</summary>
    public static DomainWorkflowStatus ToDomain(this LegacyWorkflowStatus status) =>
        status switch
        {
            LegacyWorkflowStatus.Draft => DomainWorkflowStatus.Draft,
            LegacyWorkflowStatus.Active => DomainWorkflowStatus.Active,
            LegacyWorkflowStatus.Paused => DomainWorkflowStatus.Paused,
            LegacyWorkflowStatus.Completed => DomainWorkflowStatus.Completed,
            LegacyWorkflowStatus.Cancelled => DomainWorkflowStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status), status, "Unknown legacy WorkflowStatus value."),
        };

    /// <summary>Maps a nullable legacy EF status to the canonical domain status.</summary>
    public static DomainWorkflowStatus? ToDomain(this LegacyWorkflowStatus? status) =>
        status.HasValue ? status.Value.ToDomain() : null;

    /// <summary>Maps a canonical domain status back to the legacy EF status.</summary>
    public static LegacyWorkflowStatus ToLegacy(this DomainWorkflowStatus status) =>
        status switch
        {
            DomainWorkflowStatus.Draft => LegacyWorkflowStatus.Draft,
            DomainWorkflowStatus.Active => LegacyWorkflowStatus.Active,
            DomainWorkflowStatus.Paused => LegacyWorkflowStatus.Paused,
            DomainWorkflowStatus.Completed => LegacyWorkflowStatus.Completed,
            DomainWorkflowStatus.Cancelled => LegacyWorkflowStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status), status, "Unknown domain WorkflowStatus value."),
        };

    /// <summary>Maps a nullable canonical domain status back to the legacy EF status.</summary>
    public static LegacyWorkflowStatus? ToLegacy(this DomainWorkflowStatus? status) =>
        status.HasValue ? status.Value.ToLegacy() : null;
}
