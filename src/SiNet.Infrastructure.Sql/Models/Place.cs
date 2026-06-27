using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Place
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? CityIcon { get; set; }

    public bool? InUse { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
}
