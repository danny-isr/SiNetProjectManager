using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectContract
{
    public int ProjectsId { get; set; }

    public decimal? SumContractValue { get; set; }
}
