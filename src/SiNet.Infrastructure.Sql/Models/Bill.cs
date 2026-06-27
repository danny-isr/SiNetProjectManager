using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Bill
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    public string? Description { get; set; }

    public decimal BillValue { get; set; }

    public DateTime BillSubmission { get; set; }

    public decimal? ApprovValue { get; set; }

    public DateTime? BillApproval { get; set; }

    public DateTime? Invoice { get; set; }

    public decimal? PaymentValue { get; set; }

    public DateTime? PaymentDate { get; set; }

    public decimal Vat { get; set; }

    public virtual Contract Contract { get; set; } = null!;
}
