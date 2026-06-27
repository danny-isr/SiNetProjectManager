using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class BankProject
{
    public int BankId { get; set; }

    public int ProjectsId { get; set; }

    public decimal Present { get; set; }
}
