using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectDeffContract
{
    public int ProjectsId { get; set; }

    public decimal? SumBillValue { get; set; }

    public decimal? SumApprovValue { get; set; }

    public decimal? SumPaymentValue { get; set; }

    public decimal? DffBilApprov { get; set; }

    public decimal? DffBilPayment { get; set; }
}
