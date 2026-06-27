using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectStatus
{
    public int Id { get; set; }

    /// <summary>
    /// Stable machine identifier (e.g. <c>LeadReceived</c>, <c>Active</c>, <c>Closed</c>).
    /// Unique. Used by workflow actions, never localized.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string? Title { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
}
