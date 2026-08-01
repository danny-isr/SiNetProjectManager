using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class JobType
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual ICollection<Bid> Bids { get; set; } = new List<Bid>();

    public virtual Siuser? Editor { get; set; }

    public virtual ICollection<PaymentsStep> PaymentsSteps { get; set; } = new List<PaymentsStep>();

    public virtual ICollection<ProjectFile> ProjectFiles { get; set; } = new List<ProjectFile>();

    public virtual ICollection<TypeOfProjectInProject> TypeOfProjectInProjects { get; set; } = new List<TypeOfProjectInProject>();

    // Task Management mappings - which TaskTypes and Statuses are allowed for this ProjectType
    public virtual ICollection<ProjectTypeTaskType> AllowedTaskTypes { get; set; } = new List<ProjectTypeTaskType>();
    public virtual ICollection<ProjectTypeStatus> AllowedStatuses { get; set; } = new List<ProjectTypeStatus>();

    // Workflow mappings - which WorkflowDefinitions are allowed for this ProjectType
    public virtual ICollection<ProjectTypeWorkflowDefinition> AllowedWorkflows { get; set; } = new List<ProjectTypeWorkflowDefinition>();

    /// <summary>B2: workflow instances that run as an independent track for this JobType.</summary>
    public virtual ICollection<WorkflowInstance> WorkflowInstances { get; set; } = new List<WorkflowInstance>();

    // Per-ProjectType WorkflowStage configuration (which PLN.* stages are active/required/repeatable)
    public virtual ICollection<ProjectTypeWorkflowStage> WorkflowStages { get; set; } = new List<ProjectTypeWorkflowStage>();

    // Per-ProjectType discipline TaskTypes (e.g. Traffic, Drainage, Physical)
    public virtual ICollection<ProjectTypeDiscipline> Disciplines { get; set; } = new List<ProjectTypeDiscipline>();
}
