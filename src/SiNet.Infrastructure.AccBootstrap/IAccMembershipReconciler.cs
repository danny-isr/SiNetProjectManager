namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Lightweight facade that keeps ACC project memberships in sync with the
/// local <see cref="SiNetSQL.Models.Siuser"/> table.
/// <para>
/// The actual reconciliation work reuses <see cref="IAccProjectProvisioningService.ReconcileProjectMembersAsync"/>,
/// which is idempotent: SKIP / ADD / UPGRADE per user. This service only
/// adds debouncing and background execution so UI threads don't block.
/// </para>
/// </summary>
public interface IAccMembershipReconciler
{
    /// <summary>
    /// Signals that the SI user set (or a user's <see cref="SiNetSQL.Models.AccUserType"/>)
    /// has changed. A single background pass will reconcile ALL projects that
    /// have a valid <see cref="SiNetSQL.Models.ProjectAccMapping"/>.
    /// <para>
    /// Safe to call many times in rapid succession: coalesces to a single
    /// queued pass if one is already pending.
    /// </para>
    /// </summary>
    void EnqueueReconcileAll();
}
