using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectFileRef
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? XrefId { get; set; }

    public int? FileId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ProjectFile? File { get; set; }

    public virtual ProjectFile? Xref { get; set; }
}
