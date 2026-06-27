using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Contract
{
    public int Id { get; set; }

    public int BidId { get; set; }

    public decimal ContractValue { get; set; }

    public DateTime? ContractApproval { get; set; }

    public decimal Vat { get; set; }

    public string Description { get; set; } = null!;

    public virtual Bid Bid { get; set; } = null!;

    public virtual ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
