using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectPlanner
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? ContactsId { get; set; }

    public int? ProjctId { get; set; }

    public int? RoleId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Contact? Contacts { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Project? Projct { get; set; }

    public virtual JobTitle? Role { get; set; }
}
