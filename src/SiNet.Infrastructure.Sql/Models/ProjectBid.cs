using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectBid
{
    public int ProjectsId { get; set; }

    public decimal? SumBidValue { get; set; }
}
