using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Project
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public float? Number { get; set; }

    public int? CompanyId { get; set; }

    public string? Worker { get; set; }

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public string? Admin { get; set; }

    public int? PlaceId { get; set; }

    public string? ProjectPath { get; set; }

    public bool? EndOfProject { get; set; }

    public string? NameAndNumber { get; set; }

    public int? OnerProjectId { get; set; }

    public float? MazcirotTik { get; set; }

    public int? ProjectStatusId { get; set; }

    public string? PriceQuoteDescription { get; set; }

    public int? ContactsId { get; set; }

    public string? ApproveDescription { get; set; }

    public DateTime? ApproveDate { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual ICollection<Bid> Bids { get; set; } = new List<Bid>();

    public virtual Company? Company { get; set; }

    public virtual Contact? Contacts { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<Project> InverseOnerProject { get; set; } = new List<Project>();

    public virtual Project? OnerProject { get; set; }

    public virtual ICollection<PaymentsStep> PaymentsSteps { get; set; } = new List<PaymentsStep>();

    public virtual Place? Place { get; set; }

    public virtual ICollection<ProjectAssignment> ProjectAssignments { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<ProjectPlanner> ProjectPlanners { get; set; } = new List<ProjectPlanner>();

    public virtual ProjectStatus? ProjectStatus { get; set; }

    public virtual ICollection<TypeOfProjectInProject> TypeOfProjectInProjects { get; set; } = new List<TypeOfProjectInProject>();

    /// <summary>Design alternatives belonging to this project (project-level scope).</summary>
    public virtual ICollection<ProjectAlternative> Alternatives { get; set; } = new List<ProjectAlternative>();

    public virtual ICollection<WeekWork> WeekWorks { get; set; } = new List<WeekWork>();

    public virtual ICollection<WorkHour> WorkHours { get; set; } = new List<WorkHour>();

    public virtual ICollection<InspectionReport> InspectionReports { get; set; } = new List<InspectionReport>();

    public virtual ICollection<InspectionSeries> InspectionSeries { get; set; } = new List<InspectionSeries>();

    public virtual ICollection<WorkflowInstance> WorkflowInstances { get; set; } = new List<WorkflowInstance>();
}
