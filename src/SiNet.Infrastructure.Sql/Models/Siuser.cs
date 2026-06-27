using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Siuser
{
    public int Id { get; set; }

    public string? Sid { get; set; }

    public string? Name { get; set; }

    public string? LoginName { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public bool? IsDomainGroup { get; set; }

    /// <summary>
    /// Whether the user account is active. Inactive users are excluded from
    /// all lookups, dropdowns, and assignment resolution. Defaults to <c>true</c>.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Announcement> AnnouncementAuthors { get; set; } = new List<Announcement>();

    public virtual ICollection<Announcement> AnnouncementEditors { get; set; } = new List<Announcement>();

    public virtual ICollection<Bank> BankAuthors { get; set; } = new List<Bank>();

    public virtual ICollection<Bank> BankEditors { get; set; } = new List<Bank>();

    public virtual ICollection<Company> CompanyAuthors { get; set; } = new List<Company>();

    public virtual ICollection<Company> CompanyEditors { get; set; } = new List<Company>();

    public virtual ICollection<Contact> ContactAuthors { get; set; } = new List<Contact>();

    public virtual ICollection<Contact> ContactEditors { get; set; } = new List<Contact>();

    public virtual ICollection<DrawingTypeAndLayersTable> DrawingTypeAndLayersTableAuthors { get; set; } = new List<DrawingTypeAndLayersTable>();

    public virtual ICollection<DrawingTypeAndLayersTable> DrawingTypeAndLayersTableEditors { get; set; } = new List<DrawingTypeAndLayersTable>();

    public virtual ICollection<DrawingType> DrawingTypeAuthors { get; set; } = new List<DrawingType>();

    public virtual ICollection<DrawingType> DrawingTypeEditors { get; set; } = new List<DrawingType>();

    public virtual ICollection<JobTitle> JobTitleAuthors { get; set; } = new List<JobTitle>();

    public virtual ICollection<JobTitle> JobTitleEditors { get; set; } = new List<JobTitle>();

    public virtual ICollection<JobType> JobTypeAuthors { get; set; } = new List<JobType>();

    public virtual ICollection<JobType> JobTypeEditors { get; set; } = new List<JobType>();

    public virtual ICollection<Layer> LayerAuthors { get; set; } = new List<Layer>();

    public virtual ICollection<Layer> LayerEditors { get; set; } = new List<Layer>();

    public virtual ICollection<LayerObjectType> LayerObjectTypeAuthors { get; set; } = new List<LayerObjectType>();

    public virtual ICollection<LayerObjectType> LayerObjectTypeEditors { get; set; } = new List<LayerObjectType>();

    public virtual ICollection<MavatBlock> MavatBlockAuthors { get; set; } = new List<MavatBlock>();

    public virtual ICollection<MavatBlock> MavatBlockEditors { get; set; } = new List<MavatBlock>();

    public virtual ICollection<Mifrat> MifratAuthors { get; set; } = new List<Mifrat>();

    public virtual ICollection<Mifrat> MifratEditors { get; set; } = new List<Mifrat>();

    public virtual ICollection<PaymentsStep> PaymentsStepAuthors { get; set; } = new List<PaymentsStep>();

    public virtual ICollection<PaymentsStep> PaymentsStepEditors { get; set; } = new List<PaymentsStep>();

    public virtual ICollection<Place> PlaceAuthors { get; set; } = new List<Place>();

    public virtual ICollection<Place> PlaceEditors { get; set; } = new List<Place>();

    public virtual ICollection<ProjectAssignment> ProjectAssignmentAssignedTos { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<ProjectAssignment> ProjectAssignmentAuthors { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<ProjectAssignment> ProjectAssignmentEditors { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<ProjectAssignment> ProjectAssignmentTaskGroups { get; set; } = new List<ProjectAssignment>();

    public virtual ICollection<Project> ProjectAuthors { get; set; } = new List<Project>();

    public virtual ICollection<Project> ProjectEditors { get; set; } = new List<Project>();

    public virtual ICollection<ProjectFile> ProjectFileAuthors { get; set; } = new List<ProjectFile>();

    public virtual ICollection<ProjectFile> ProjectFileEditors { get; set; } = new List<ProjectFile>();

    public virtual ICollection<ProjectFileRef> ProjectFileRefAuthors { get; set; } = new List<ProjectFileRef>();

    public virtual ICollection<ProjectFileRef> ProjectFileRefEditors { get; set; } = new List<ProjectFileRef>();

    public virtual ICollection<ProjectFolder> ProjectFolderAuthors { get; set; } = new List<ProjectFolder>();

    public virtual ICollection<ProjectFolder> ProjectFolderEditors { get; set; } = new List<ProjectFolder>();

    public virtual ICollection<ProjectPlanner> ProjectPlannerAuthors { get; set; } = new List<ProjectPlanner>();

    public virtual ICollection<ProjectPlanner> ProjectPlannerEditors { get; set; } = new List<ProjectPlanner>();

    public virtual ICollection<ProjectStatus> ProjectStatusAuthors { get; set; } = new List<ProjectStatus>();

    public virtual ICollection<ProjectStatus> ProjectStatusEditors { get; set; } = new List<ProjectStatus>();

    public virtual ICollection<Property> PropertyAuthors { get; set; } = new List<Property>();

    public virtual ICollection<Property> PropertyEditors { get; set; } = new List<Property>();

    public virtual ICollection<ServiceProvider> ServiceProviderAuthors { get; set; } = new List<ServiceProvider>();

    public virtual ICollection<ServiceProvider> ServiceProviderEditors { get; set; } = new List<ServiceProvider>();

    public virtual ICollection<TabaDatum> TabaDatumAuthors { get; set; } = new List<TabaDatum>();

    public virtual ICollection<TabaDatum> TabaDatumEditors { get; set; } = new List<TabaDatum>();

    public virtual ICollection<TypeOfProjectInProject> TypeOfProjectInProjectAdminWorkers { get; set; } = new List<TypeOfProjectInProject>();

    public virtual ICollection<TypeOfProjectInProject> TypeOfProjectInProjectAuthors { get; set; } = new List<TypeOfProjectInProject>();

    public virtual ICollection<TypeOfProjectInProject> TypeOfProjectInProjectEditors { get; set; } = new List<TypeOfProjectInProject>();

    public virtual ICollection<WeekWork> WeekWorkAuthors { get; set; } = new List<WeekWork>();

    public virtual ICollection<WeekWork> WeekWorkEditors { get; set; } = new List<WeekWork>();

    public virtual ICollection<WeekWork> WeekWorkWorkers { get; set; } = new List<WeekWork>();

    public virtual ICollection<WorkHour> WorkHourAuthors { get; set; } = new List<WorkHour>();

    public virtual ICollection<WorkHour> WorkHourEditors { get; set; } = new List<WorkHour>();

    // === NEW: Task Management Navigation ===

    /// <summary>
    /// Events created by this user.
    /// </summary>
    public virtual ICollection<ProjectAssignmentEvent> ProjectAssignmentEventsCreatedBy { get; set; } = new List<ProjectAssignmentEvent>();

    /// <summary>
    /// User settings (one-to-one).
    /// </summary>
    public virtual UserSetting? UserSetting { get; set; }

    /// <summary>
    /// Per-user status color overrides.
    /// </summary>
    public virtual ICollection<UserStatusPreference> UserStatusPreferences { get; set; } = new List<UserStatusPreference>();

    // === User Groups ===

    /// <summary>
    /// Groups this user belongs to (e.g. ניהול משרד, הנהלה בכירה, מתכננים).
    /// </summary>
    public virtual ICollection<UserGroupMembership> GroupMemberships { get; set; } = new List<UserGroupMembership>();

    // === ACC (Autodesk Construction Cloud) Properties ===

    /// <summary>
    /// Inspection reports where this user was the inspector.
    /// </summary>
    public virtual ICollection<InspectionReport> InspectionReportsAsInspector { get; set; } = new List<InspectionReport>();

    /// <summary>
    /// The user's ACC (Autodesk Construction Cloud) access type.
    /// Determines permissions when bootstrapping projects to ACC.
    /// Default: NoAccUser (0) - user has no ACC access.
    /// </summary>
    public AccUserType AccUserType { get; set; }

    /// <summary>
    /// The user's application role (authorization level).
    /// Default: Employee (1) - regular worker access.
    /// </summary>
    public AppUserRole Role { get; set; } = AppUserRole.Employee;

    /// <summary>Maps to the MasterPlan Employee ID for R03 attendance comparison.</summary>
    public int? MasterPlanEmployeeId { get; set; }
}
