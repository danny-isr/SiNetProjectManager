using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Bank
{
    public int Id { get; set; }

    public DateTime? Date { get; set; }

    public float? Page { get; set; }

    public float? Ref { get; set; }

    public string? Description { get; set; }

    public decimal? Mandatory { get; set; }

    public decimal? Rights { get; set; }

    public decimal? Balance { get; set; }

    public string? AccountNumber { get; set; }

    public string? DescriptionDuty { get; set; }

    public string? OldProject { get; set; }

    public int? PayFromId { get; set; }

    public int? PayToId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public string? DescriptionBank { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Company? PayFrom { get; set; }

    public virtual Company? PayTo { get; set; }

    public virtual ICollection<PaymentsStep> PaymentsSteps { get; set; } = new List<PaymentsStep>();
}
