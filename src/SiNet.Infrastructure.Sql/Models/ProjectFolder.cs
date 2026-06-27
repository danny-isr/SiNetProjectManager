using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectFolder
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? Infolderid { get; set; }

    public float? SecurityLevel { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ProjectFolder? Infolder { get; set; }

    public virtual ICollection<ProjectFolder> InverseInfolder { get; set; } = new List<ProjectFolder>();

    public virtual ICollection<ProjectFile> ProjectFiles { get; set; } = new List<ProjectFile>();
}
