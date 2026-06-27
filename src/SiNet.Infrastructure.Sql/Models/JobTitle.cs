using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class JobTitle
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<ProjectPlanner> ProjectPlanners { get; set; } = new List<ProjectPlanner>();
}
