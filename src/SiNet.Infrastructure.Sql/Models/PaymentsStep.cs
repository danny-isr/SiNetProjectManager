using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class PaymentsStep
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public float? StepNumber { get; set; }

    public int? ProjectId { get; set; }

    public decimal? ContractValue { get; set; }

    public int? JobTypeId { get; set; }

    public float? Percent { get; set; }

    public decimal? ExpectedStepPayment { get; set; }

    public DateTime? ExpectedPaymentDate { get; set; }

    public DateTime? BillSubmission { get; set; }

    public DateTime? BillApproval { get; set; }

    public float? ApprovalPercent { get; set; }

    public decimal? ApprovalStepPayment { get; set; }

    public DateTime? Invoice { get; set; }

    public DateTime? PaymentDate { get; set; }

    public decimal? StepPayment { get; set; }

    public int? BankId { get; set; }

    public string? Description { get; set; }

    public DateTime? ContractDate { get; set; }

    public float? Vat { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Bank? Bank { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual JobType? JobType { get; set; }

    public virtual Project? Project { get; set; }
}
