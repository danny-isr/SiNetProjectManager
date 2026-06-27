using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class WeekWork
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public float? WorkHours { get; set; }

    public string? Title { get; set; }

    public DateTime? Week { get; set; }

    public float? Priority { get; set; }

    public string? JobStatus { get; set; }

    public int? WorkerId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Project? Project { get; set; }

    public virtual Siuser? Worker { get; set; }
}
