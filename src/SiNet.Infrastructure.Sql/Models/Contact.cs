using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Contact
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? FirstName { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public int? JobTitleId { get; set; }

    public string? WorkPhone { get; set; }

    public int? CompanyId { get; set; }

    public string? HomePhone { get; set; }

    public string? CellPhone { get; set; }

    public string? WorkFax { get; set; }

    public string? WorkAddress { get; set; }

    public string? WorkCity { get; set; }

    public string? WorkState { get; set; }

    public string? WorkZip { get; set; }

    public string? WorkCountry { get; set; }

    public string? WebPage { get; set; }

    public string? Comments { get; set; }

    public string? Status { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    /// <summary>Indicates whether the contact is active. Inactive contacts are excluded from everyday workflows.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Maps to the MasterPlan Contact ID for one-way sync.</summary>
    public int? MasterPlanContactId { get; set; }

    /// <summary>When true, CrossSync overwrites contact fields from MasterPlan on each daily sync.</summary>
    public bool MasterPlanSync { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Company? Company { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual JobTitle? JobTitle { get; set; }

    public virtual ICollection<ProjectPlanner> ProjectPlanners { get; set; } = new List<ProjectPlanner>();

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    // === NEW: Task Management Navigation ===
    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEvents { get; set; } = new List<ProjectAssignmentEvent>();
}
