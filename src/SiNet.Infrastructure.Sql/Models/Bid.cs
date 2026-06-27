using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Bid
{
    public int Id { get; set; }

    public int ProjectsId { get; set; }

    public int JobTypeId { get; set; }

    public decimal BidValue { get; set; }

    public DateTime BidSubmission { get; set; }

    public decimal Vat { get; set; }

    public string Description { get; set; } = null!;

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual JobType JobType { get; set; } = null!;

    public virtual Project Projects { get; set; } = null!;
}
