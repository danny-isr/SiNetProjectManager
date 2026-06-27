using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectBill
{
    public int ProjectsId { get; set; }

    public decimal? SumBillValue { get; set; }

    public decimal? SumApprovValue { get; set; }

    public decimal? SumPaymentValue { get; set; }
}
