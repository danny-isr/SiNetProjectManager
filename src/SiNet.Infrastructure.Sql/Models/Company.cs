using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Company
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? Email { get; set; }

    public string? JobTitle { get; set; }

    public string? WorkPhone { get; set; }

    public string? CellPhone { get; set; }

    public string? WorkFax { get; set; }

    public string? WorkAddress { get; set; }

    public string? WorkCity { get; set; }

    public string? WorkState { get; set; }

    public string? WorkZip { get; set; }

    public string? WorkCountry { get; set; }

    public string? WebPage { get; set; }

    public string? Comments { get; set; }

    public bool? NotCompany { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    /// <summary>Indicates whether the company is active. Inactive companies are excluded from everyday workflows.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Maps to the MasterPlan Company ID for one-way sync.</summary>
    public int? MasterPlanCompanyId { get; set; }

    /// <summary>Israeli company registration number (ח.פ.) — synced from MasterPlan.</summary>
    public string? RegistrationNumber { get; set; }

    /// <summary>When true, CrossSync overwrites company fields from MasterPlan on each daily sync.</summary>
    public bool MasterPlanSync { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual ICollection<Bank> BankPayFroms { get; set; } = new List<Bank>();

    public virtual ICollection<Bank> BankPayTos { get; set; } = new List<Bank>();

    public virtual ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    // === NEW: Task Management Navigation ===
    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEvents { get; set; } = new List<ProjectAssignmentEvent>();
}
