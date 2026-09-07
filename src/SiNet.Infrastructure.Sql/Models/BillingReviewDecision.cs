namespace SiNetSQL.Models;

/// <summary>
/// Current SiNet local billing review decision for one MasterPlan project.
/// External <see cref="ProjectId"/> is Replica/MasterPlan identity — not a SiNet Project FK.
/// </summary>
public sealed class BillingReviewDecision
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string DecisionType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? ReviewAgainDate { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int CreatedByUserId { get; set; }
    public string? CreatedByLogin { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedByUserId { get; set; }
    public string? UpdatedByLogin { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
    public int? ClearedByUserId { get; set; }
    public string? ClearedByLogin { get; set; }

    public int? ObservedLatestBillId { get; set; }
    public int? ObservedLatestBillStatusId { get; set; }
    public DateTime? ObservedLastBillDate { get; set; }
    public decimal? ObservedHoursSinceLastBill { get; set; }
    public DateTime? ObservedLastWorkDate { get; set; }
}
