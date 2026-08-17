using Microsoft.EntityFrameworkCore;
using SiNetSQL.Models;

namespace SiNetSQL.Data;

public partial class SiNetSQLDbContext : DbContext
{
    public SiNetSQLDbContext()
    {
    }

    public SiNetSQLDbContext(DbContextOptions<SiNetSQLDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Announcement> Announcements { get; set; }

    public virtual DbSet<Bank> Banks { get; set; }

    public virtual DbSet<BankProject> BankProjects { get; set; }

    public virtual DbSet<Bid> Bids { get; set; }

    public virtual DbSet<Bill> Bills { get; set; }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<Contact> Contacts { get; set; }

    public virtual DbSet<Contract> Contracts { get; set; }

    public virtual DbSet<DrawingType> DrawingTypes { get; set; }

    public virtual DbSet<DrawingTypeAndLayersTable> DrawingTypeAndLayersTables { get; set; }

    public virtual DbSet<JobTitle> JobTitles { get; set; }

    public virtual DbSet<JobType> JobTypes { get; set; }

    public virtual DbSet<Layer> Layers { get; set; }

    public virtual DbSet<LayerObjectType> LayerObjectTypes { get; set; }

    public virtual DbSet<MavatBlock> MavatBlocks { get; set; }

    public virtual DbSet<Mifrat> Mifrats { get; set; }

    public virtual DbSet<PaymentsStep> PaymentsSteps { get; set; }

    public virtual DbSet<Place> Places { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectAssignment> ProjectAssignments { get; set; }

    public virtual DbSet<ProjectBid> ProjectBids { get; set; }

    public virtual DbSet<ProjectBill> ProjectBills { get; set; }

    public virtual DbSet<ProjectContract> ProjectContracts { get; set; }

    public virtual DbSet<ProjectDeffContract> ProjectDeffContracts { get; set; }

    public virtual DbSet<ProjectFile> ProjectFiles { get; set; }

    public virtual DbSet<ProjectFileRef> ProjectFileRefs { get; set; }

    public virtual DbSet<ProjectFolder> ProjectFolders { get; set; }

    public virtual DbSet<ProjectPlanner> ProjectPlanners { get; set; }

    public virtual DbSet<ProjectStatus> ProjectStatuses { get; set; }

    public virtual DbSet<Property> Properties { get; set; }

    public virtual DbSet<ServiceProvider> ServiceProviders { get; set; }

    public virtual DbSet<Siuser> Siusers { get; set; }

    public virtual DbSet<TabaDatum> TabaData { get; set; }

    public virtual DbSet<ThreadStatusMapping> ThreadStatusMappings { get; set; }

    public virtual DbSet<TypeOfProjectInProject> TypeOfProjectInProjects { get; set; }

    public virtual DbSet<WeekWork> WeekWorks { get; set; }

    public virtual DbSet<WorkHour> WorkHours { get; set; }

    // === NEW: Task Management DbSets ===
    public virtual DbSet<TaskType> TaskTypes { get; set; }

    public virtual DbSet<ProjectAssignmentStatus> ProjectAssignmentStatuses { get; set; }

    public virtual DbSet<ProjectAssignmentEvent> ProjectAssignmentEvents { get; set; }

    public virtual DbSet<UserSetting> UserSettings { get; set; }

    // === NEW: ProjectType-based filtering mappings ===
    public virtual DbSet<ProjectTypeTaskType> ProjectTypeTaskTypes { get; set; }

    public virtual DbSet<ProjectTypeStatus> ProjectTypeStatuses { get; set; }

    public virtual DbSet<UserStatusPreference> UserStatusPreferences { get; set; }

    // === NEW: Task-to-Project Status Mapping ===
    public virtual DbSet<TaskStatusToProjectStatusMapping> TaskStatusToProjectStatusMappings { get; set; }

    // === NEW: Project Decisions System ===
    public virtual DbSet<DecisionCategory> DecisionCategories { get; set; }
    public virtual DbSet<ProjectDecision> ProjectDecisions { get; set; }
    public virtual DbSet<DecisionHistory> DecisionHistories { get; set; }

    // === NEW: Email Inbox Ingestion DbSets ===
    public virtual DbSet<EmailInboxMessage> EmailInboxMessages { get; set; }

    public virtual DbSet<EmailInboxAttachment> EmailInboxAttachments { get; set; }

    // === NEW: ACC (Autodesk Construction Cloud) Mapping DbSets ===
    public virtual DbSet<AccHub> AccHubs { get; set; }

    public virtual DbSet<AccSystemResource> AccSystemResources { get; set; }

    public virtual DbSet<ProjectAccMapping> ProjectAccMappings { get; set; }

    // === NEW: Sync Engine Failure Logging ===
    public virtual DbSet<SyncRunFailure> SyncRunFailures { get; set; }

    // === NEW: Centralized System Settings ===
    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    // === NEW: Inspection System DbSets ===
    public virtual DbSet<InspectionReport> InspectionReports { get; set; }

    public virtual DbSet<Chapter> Chapters { get; set; }

    public virtual DbSet<ChapterName> ChapterNames { get; set; }

    public virtual DbSet<SectionName> SectionNames { get; set; }

    public virtual DbSet<Section> Sections { get; set; }

    public virtual DbSet<CommentsBank> CommentsBank { get; set; }

    public virtual DbSet<InspectionNote> InspectionNotes { get; set; }

    public virtual DbSet<InspectionNoteStatus> InspectionNoteStatuses { get; set; }

    public virtual DbSet<InspectionSeries> InspectionSeries { get; set; }

    public virtual DbSet<InspectionReportDrawing> InspectionReportDrawings { get; set; }

    public virtual DbSet<InspectionSeriesFileConfig> InspectionSeriesFileConfigs { get; set; }

    public virtual DbSet<InspectionReportSnapshot> InspectionReportSnapshots { get; set; }

    public virtual DbSet<InspectionNoteAttachment> InspectionNoteAttachments { get; set; }

    public virtual DbSet<InspectionReportReviewedFile> InspectionReportReviewedFiles { get; set; }

    // === NEW: Task Linking System ===
    public virtual DbSet<TaskLink> TaskLinks { get; set; }

    // === NEW: Workflow System ===
    public virtual DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
    public virtual DbSet<WorkflowStageDefinition> WorkflowStageDefinitions { get; set; }
    public virtual DbSet<WorkflowTransitionRule> WorkflowTransitionRules { get; set; }
    public virtual DbSet<WorkflowInstance> WorkflowInstances { get; set; }
    public virtual DbSet<WorkflowStageTransition> WorkflowStageTransitions { get; set; }

    // === NEW: Action Permission System ===
    public virtual DbSet<ActionPermission> ActionPermissions { get; set; }

    // === NEW: Workflow Stage ↔ Task Mapping ===
    public virtual DbSet<WorkflowStageTask> WorkflowStageTasks { get; set; }
    public virtual DbSet<WorkflowTransitionAction> WorkflowTransitionActions { get; set; }
    public virtual DbSet<WorkflowStartTrigger> WorkflowStartTriggers { get; set; }

    // === NEW: ProjectType ↔ WorkflowDefinition Mapping ===
    public virtual DbSet<ProjectTypeWorkflowDefinition> ProjectTypeWorkflowDefinitions { get; set; }

    // === NEW: Task Behavior System ===
    public virtual DbSet<TaskBehaviorDefinition> TaskBehaviorDefinitions { get; set; }
    public virtual DbSet<TaskTriggerRule> TaskTriggerRules { get; set; }
    public virtual DbSet<TaskCompletionRule> TaskCompletionRules { get; set; }

    // === NEW: Project Alternative System ===
    public virtual DbSet<ProjectAlternative> ProjectAlternatives { get; set; }

    // === NEW: User Groups System ===
    public virtual DbSet<UserGroup> UserGroups { get; set; }
    public virtual DbSet<UserGroupMembership> UserGroupMemberships { get; set; }

    // ── Planning Workflow Taxonomy ──
    public virtual DbSet<TaskResultDefinition> TaskResultDefinitions { get; set; }
    public virtual DbSet<ProjectTypeWorkflowStage> ProjectTypeWorkflowStages { get; set; }
    public virtual DbSet<ProjectTypeDiscipline> ProjectTypeDisciplines { get; set; }

    /// <summary>
    /// Design-time only fallback for EF migrations (Add-Migration, Update-Database).
    /// At runtime, options are provided via DI (AddDbContextFactory in App.xaml.cs)
    /// using the connection string from Windows Credential Manager.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Fallback for design-time tools only (PMC: Add-Migration, Update-Database).
            // At runtime, DI provides the connection string from the vault.
            // UseCompatibilityLevel(120) prevents EF Core 8 from generating OPENJSON-based SQL
            // for Contains() calls, which requires DB compat level >= 130 (SQL Server 2016).
            optionsBuilder.UseSqlServer(
                "Data Source=SI-WIN-2K19\\SIDATA;Initial Catalog=SIData;Integrated Security=True;TrustServerCertificate=True;",
                o => o.UseCompatibilityLevel(120));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("Announcements_Author");
                    tb.HasTrigger("Announcements_Created");
                    tb.HasTrigger("Announcements_Editor");
                    tb.HasTrigger("Announcements_Modified");
                });

            entity.HasIndex(e => e.Title, "Announcements_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Body).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Expires).HasColumnType("datetime");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.AnnouncementAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Announcements_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.AnnouncementEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Announcements_User_Editor");
        });

        modelBuilder.Entity<Bank>(entity =>
        {
            entity.ToTable("Bank", tb =>
                {
                    tb.HasTrigger("Bank_Author");
                    tb.HasTrigger("Bank_Created");
                    tb.HasTrigger("Bank_Editor");
                    tb.HasTrigger("Bank_Modified");
                });

            entity.HasIndex(e => new { e.Date, e.Description, e.Mandatory, e.Rights, e.Balance, e.Ref }, "IX_Bank").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AccountNumber)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Balance)
                .HasDefaultValue(0m)
                .HasColumnType("money");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.Date).HasColumnType("datetime");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.DescriptionBank).HasMaxLength(255);
            entity.Property(e => e.DescriptionDuty)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Mandatory)
                .HasDefaultValue(0m)
                .HasColumnType("money");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.OldProject)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.PayFromId).HasColumnName("PayFromID");
            entity.Property(e => e.PayToId).HasColumnName("PayToID");
            entity.Property(e => e.Rights)
                .HasDefaultValue(0m)
                .HasColumnType("money");

            entity.HasOne(d => d.Author).WithMany(p => p.BankAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Bank_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.BankEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Bank_User_Editor");

            entity.HasOne(d => d.PayFrom).WithMany(p => p.BankPayFroms)
                .HasForeignKey(d => d.PayFromId)
                .HasConstraintName("FK_SI_Bank_Company_PayFrom");

            entity.HasOne(d => d.PayTo).WithMany(p => p.BankPayTos)
                .HasForeignKey(d => d.PayToId)
                .HasConstraintName("FK_SI_Bank_Company_PayTo");
        });

        modelBuilder.Entity<BankProject>(entity =>
        {
            entity.HasKey(e => new { e.BankId, e.ProjectsId }).HasName("Bank_Projects_Index");

            entity.ToTable("Bank_Projects");

            entity.Property(e => e.BankId).HasColumnName("BankID");
            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.Present)
                .HasDefaultValue(1.0m)
                .HasColumnType("numeric(10, 1)");
        });

        modelBuilder.Entity<Bid>(entity =>
        {
            entity.ToTable("Bid");

            entity.HasIndex(e => new { e.ProjectsId, e.JobTypeId }, "IX_Bid").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.BidSubmission)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.BidValue).HasColumnType("money");
            entity.Property(e => e.Description)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.JobTypeId).HasColumnName("JobTypeID");
            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.Vat)
                .HasDefaultValue(0.18m)
                .HasColumnType("numeric(3, 3)")
                .HasColumnName("VAT");

            entity.HasOne(d => d.JobType).WithMany(p => p.Bids)
                .HasForeignKey(d => d.JobTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SI_Bid_JobType");

            entity.HasOne(d => d.Projects).WithMany(p => p.Bids)
                .HasForeignKey(d => d.ProjectsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SI_Bid_Projects");
        });

        modelBuilder.Entity<Bill>(entity =>
        {
            entity.ToTable("Bill");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ApprovValue).HasColumnType("money");
            entity.Property(e => e.BillApproval).HasColumnType("datetime");
            entity.Property(e => e.BillSubmission)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.BillValue).HasColumnType("money");
            entity.Property(e => e.ContractId).HasColumnName("ContractID");
            entity.Property(e => e.Description)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Invoice)
                .HasColumnType("datetime")
                .HasColumnName("invoice");
            entity.Property(e => e.PaymentDate).HasColumnType("datetime");
            entity.Property(e => e.PaymentValue).HasColumnType("money");
            entity.Property(e => e.Vat)
                .HasDefaultValue(0.18m)
                .HasColumnType("numeric(3, 3)")
                .HasColumnName("VAT");

            entity.HasOne(d => d.Contract).WithMany(p => p.Bills)
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SI_Bill_Contract");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Company", tb =>
                {
                    tb.HasTrigger("Company_Author");
                    tb.HasTrigger("Company_Created");
                    tb.HasTrigger("Company_Editor");
                    tb.HasTrigger("Company_Modified");
                });

            entity.HasIndex(e => e.Title, "Company_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.CellPhone)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Comments).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.JobTitle)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WebPage)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkAddress).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkCity)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkCountry)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkFax)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkPhone)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkState)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkZip)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.CompanyAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Company_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.CompanyEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Company_User_Editor");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.RegistrationNumber)
                .HasMaxLength(50)
                .UseCollation("Hebrew_100_CI_AS");

            entity.Property(e => e.MasterPlanSync)
                .HasDefaultValue(false);

            entity.HasIndex(e => e.MasterPlanCompanyId)
                .IsUnique()
                .HasFilter("[MasterPlanCompanyId] IS NOT NULL");
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("Contacts_Author");
                    tb.HasTrigger("Contacts_Created");
                    tb.HasTrigger("Contacts_Editor");
                    tb.HasTrigger("Contacts_FirstLasteName");
                    tb.HasTrigger("Contacts_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.CellPhone)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Comments).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.FirstName)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.HomePhone)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.JobTitleId).HasColumnName("JobTitleID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WebPage)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkAddress).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkCity)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkCountry)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkFax)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkPhone)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkState)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkZip)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.ContactAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Contacts_User_Author");

            entity.HasOne(d => d.Company).WithMany(p => p.Contacts)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK_SI_Contacts_Company_Company");

            entity.HasOne(d => d.Editor).WithMany(p => p.ContactEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Contacts_User_Editor");

            entity.HasOne(d => d.JobTitle).WithMany(p => p.Contacts)
                .HasForeignKey(d => d.JobTitleId)
                .HasConstraintName("FK_SI_Contacts_jobTitle_JobTitle");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.MasterPlanSync)
                .HasDefaultValue(false);

            entity.HasIndex(e => e.MasterPlanContactId)
                .IsUnique()
                .HasFilter("[MasterPlanContactId] IS NOT NULL");
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.ToTable("Contract");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.BidId).HasColumnName("BidID");
            entity.Property(e => e.ContractApproval)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ContractValue).HasColumnType("money");
            entity.Property(e => e.Description)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Vat)
                .HasDefaultValue(0.18m)
                .HasColumnType("numeric(3, 3)")
                .HasColumnName("VAT");

            entity.HasOne(d => d.Bid).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.BidId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SI_Contract_Bid");
        });

        modelBuilder.Entity<DrawingType>(entity =>
        {
            entity.ToTable("DrawingType", tb =>
                {
                    tb.HasTrigger("DrawingType_Author");
                    tb.HasTrigger("DrawingType_Created");
                    tb.HasTrigger("DrawingType_Editor");
                    tb.HasTrigger("DrawingType_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.MifratId).HasColumnName("MifratID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.DrawingTypeAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_DrawingType_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.DrawingTypeEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_DrawingType_User_Editor");

            entity.HasOne(d => d.Mifrat).WithMany(p => p.DrawingTypes)
                .HasForeignKey(d => d.MifratId)
                .HasConstraintName("FK_SI_DrawingType_Mifrat_Mifrat");
        });

        modelBuilder.Entity<DrawingTypeAndLayersTable>(entity =>
        {
            entity.ToTable("DrawingTypeAndLayersTable", tb =>
                {
                    tb.HasTrigger("DrawingTypeAndLayersTable_Author");
                    tb.HasTrigger("DrawingTypeAndLayersTable_Created");
                    tb.HasTrigger("DrawingTypeAndLayersTable_Editor");
                    tb.HasTrigger("DrawingTypeAndLayersTable_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.GropNameId).HasColumnName("GropNameID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.ObjectsNameId).HasColumnName("ObjectsNameID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.DrawingTypeAndLayersTableAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_DrawingTypeAndLayersTable_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.DrawingTypeAndLayersTableEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_DrawingTypeAndLayersTable_User_Editor");

            entity.HasOne(d => d.GropName).WithMany(p => p.DrawingTypeAndLayersTables)
                .HasForeignKey(d => d.GropNameId)
                .HasConstraintName("FK_SI_DrawingTypeAndLayersTable_DrawingType_GropName");

            entity.HasOne(d => d.ObjectsName).WithMany(p => p.DrawingTypeAndLayersTables)
                .HasForeignKey(d => d.ObjectsNameId)
                .HasConstraintName("FK_SI_DrawingTypeAndLayersTable_Layers_ObjectsName");
        });

        modelBuilder.Entity<JobTitle>(entity =>
        {
            entity.ToTable("jobTitle", tb =>
                {
                    tb.HasTrigger("jobTitle_Author");
                    tb.HasTrigger("jobTitle_Created");
                    tb.HasTrigger("jobTitle_Editor");
                    tb.HasTrigger("jobTitle_Modified");
                });

            entity.HasIndex(e => e.Title, "jobTitle_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.JobTitleAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_jobTitle_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.JobTitleEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_jobTitle_User_Editor");
        });

        modelBuilder.Entity<JobType>(entity =>
        {
            entity.ToTable("JobType", tb =>
                {
                    tb.HasTrigger("JobType_Author");
                    tb.HasTrigger("JobType_Created");
                    tb.HasTrigger("JobType_Editor");
                    tb.HasTrigger("JobType_Modified");
                });

            entity.HasIndex(e => e.Title, "JobType_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.JobTypeAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_JobType_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.JobTypeEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_JobType_User_Editor");
        });

        modelBuilder.Entity<Layer>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("Layers_Author");
                    tb.HasTrigger("Layers_Created");
                    tb.HasTrigger("Layers_Editor");
                    tb.HasTrigger("Layers_Modified");
                });

            entity.HasIndex(e => e.Title, "Layers_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.LayerName)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.LayerObjectTypeId).HasColumnName("LayerObjectTypeID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.LayerAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Layers_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.LayerEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Layers_User_Editor");

            entity.HasOne(d => d.LayerObjectType).WithMany(p => p.Layers)
                .HasForeignKey(d => d.LayerObjectTypeId)
                .HasConstraintName("FK_SI_Layers_LayerObjectType_LayerObjectType");
        });

        modelBuilder.Entity<LayerObjectType>(entity =>
        {
            entity.ToTable("LayerObjectType", tb =>
                {
                    tb.HasTrigger("LayerObjectType_Author");
                    tb.HasTrigger("LayerObjectType_Created");
                    tb.HasTrigger("LayerObjectType_Editor");
                    tb.HasTrigger("LayerObjectType_Modified");
                });

            entity.HasIndex(e => e.Title, "LayerObjectType_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.LayerObjectTypeAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_LayerObjectType_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.LayerObjectTypeEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_LayerObjectType_User_Editor");
        });

        modelBuilder.Entity<MavatBlock>(entity =>
        {
            entity.ToTable("MavatBlock", tb =>
                {
                    tb.HasTrigger("MavatBlock_Author");
                    tb.HasTrigger("MavatBlock_Created");
                    tb.HasTrigger("MavatBlock_Editor");
                    tb.HasTrigger("MavatBlock_Modified");
                });

            entity.HasIndex(e => e.Title, "MavatBlock_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Layer)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.MavatBlockAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_MavatBlock_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.MavatBlockEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_MavatBlock_User_Editor");
        });

        modelBuilder.Entity<Mifrat>(entity =>
        {
            entity.ToTable("Mifrat", tb =>
                {
                    tb.HasTrigger("Mifrat_Author");
                    tb.HasTrigger("Mifrat_Created");
                    tb.HasTrigger("Mifrat_Editor");
                    tb.HasTrigger("Mifrat_Modified");
                });

            entity.HasIndex(e => e.Title, "Mifrat_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.LayerName)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.MifratAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Mifrat_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.MifratEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Mifrat_User_Editor");
        });

        modelBuilder.Entity<PaymentsStep>(entity =>
        {
            entity.ToTable("PaymentsStep", tb =>
                {
                    tb.HasTrigger("PaymentsStep_Author");
                    tb.HasTrigger("PaymentsStep_Created");
                    tb.HasTrigger("PaymentsStep_Editor");
                    tb.HasTrigger("PaymentsStep_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ApprovalStepPayment).HasColumnType("money");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.BankId).HasColumnName("bankID");
            entity.Property(e => e.BillApproval).HasColumnType("datetime");
            entity.Property(e => e.BillSubmission).HasColumnType("datetime");
            entity.Property(e => e.ContractDate).HasColumnType("datetime");
            entity.Property(e => e.ContractValue).HasColumnType("money");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.Description).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.ExpectedPaymentDate).HasColumnType("datetime");
            entity.Property(e => e.ExpectedStepPayment).HasColumnType("money");
            entity.Property(e => e.Invoice)
                .HasColumnType("datetime")
                .HasColumnName("invoice");
            entity.Property(e => e.JobTypeId).HasColumnName("JobTypeID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.PaymentDate).HasColumnType("datetime");
            entity.Property(e => e.ProjectId).HasColumnName("ProjectID");
            entity.Property(e => e.StepPayment).HasColumnType("money");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Vat).HasColumnName("VAT");

            entity.HasOne(d => d.Author).WithMany(p => p.PaymentsStepAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_PaymentsStep_User_Author");

            entity.HasOne(d => d.Bank).WithMany(p => p.PaymentsSteps)
                .HasForeignKey(d => d.BankId)
                .HasConstraintName("FK_SI_PaymentsStep_Bank_bank");

            entity.HasOne(d => d.Editor).WithMany(p => p.PaymentsStepEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_PaymentsStep_User_Editor");

            entity.HasOne(d => d.JobType).WithMany(p => p.PaymentsSteps)
                .HasForeignKey(d => d.JobTypeId)
                .HasConstraintName("FK_SI_PaymentsStep_JobType_JobType");

            entity.HasOne(d => d.Project).WithMany(p => p.PaymentsSteps)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_SI_PaymentsStep_Projects_Project");
        });

        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Place", tb =>
                {
                    tb.HasTrigger("Place_Author");
                    tb.HasTrigger("Place_Created");
                    tb.HasTrigger("Place_Editor");
                    tb.HasTrigger("Place_Modified");
                });

            entity.HasIndex(e => e.Title, "Place_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.CityIcon)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.PlaceAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Place_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.PlaceEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Place_User_Editor");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("Projects_Author");
                    tb.HasTrigger("Projects_Created");
                    tb.HasTrigger("Projects_Editor");
                    tb.HasTrigger("Projects_Modified");
                    tb.HasTrigger("Projects_NameAndNumber");
                });

            entity.HasIndex(e => e.Title, "Projects_TitleIndex").IsUnique();

            // Unique filtered index on Number (allows multiple NULLs, enforces uniqueness for non-NULL values)
            entity.HasIndex(e => e.Number)
                .IsUnique()
                .HasDatabaseName("UX_Projects_Number")
                .HasFilter("[Number] IS NOT NULL");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Admin)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.ApproveDate).HasColumnType("datetime");
            entity.Property(e => e.ApproveDescription).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.ContactsId).HasColumnName("ContactsID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.End).HasColumnType("datetime");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.NameAndNumber)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.OnerProjectId).HasColumnName("OnerProjectID");
            entity.Property(e => e.PlaceId).HasColumnName("PlaceID");
            entity.Property(e => e.PriceQuoteDescription).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.ProjectPath)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.ProjectStatusId).HasColumnName("ProjectStatusID");
            entity.Property(e => e.Start).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(46)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Worker)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("worker");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Projects_User_Author");

            entity.HasOne(d => d.Company).WithMany(p => p.Projects)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK_SI_Projects_Company_Company");

            entity.HasOne(d => d.Contacts).WithMany(p => p.Projects)
                .HasForeignKey(d => d.ContactsId)
                .HasConstraintName("FK_SI_Projects_Contacts_Contacts");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Projects_User_Editor");

            entity.HasOne(d => d.OnerProject).WithMany(p => p.InverseOnerProject)
                .HasForeignKey(d => d.OnerProjectId)
                .HasConstraintName("FK_SI_Projects_Projects_OnerProject");

            entity.HasOne(d => d.Place).WithMany(p => p.Projects)
                .HasForeignKey(d => d.PlaceId)
                .HasConstraintName("FK_SI_Projects_Place_Place");

            entity.HasOne(d => d.ProjectStatus).WithMany(p => p.Projects)
                .HasForeignKey(d => d.ProjectStatusId)
                .HasConstraintName("FK_SI_Projects_ProjectStatus_ProjectStatus");
        });

        modelBuilder.Entity<ProjectAssignment>(entity =>
        {
            entity.ToTable("ProjectAssignment", tb =>
                {
                    tb.HasTrigger("ProjectAssignment_Author");
                    tb.HasTrigger("ProjectAssignment_Created");
                    tb.HasTrigger("ProjectAssignment_Editor");
                    tb.HasTrigger("ProjectAssignment_Modified");
                });

            entity.HasIndex(e => e.Title, "IX_ProjectAssignment_Title");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AssignedToId).HasColumnName("AssignedToID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Body).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.DueDate).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Grading).HasColumnName("grading");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Priority)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.ProjectId).HasColumnName("ProjectID");
            entity.Property(e => e.StartDate).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.TaskGroupId).HasColumnName("TaskGroupID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.AssignedTo).WithMany(p => p.ProjectAssignmentAssignedTos)
                .HasForeignKey(d => d.AssignedToId)
                .HasConstraintName("FK_SI_ProjectAssignment_User_AssignedTo");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectAssignmentAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectAssignment_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectAssignmentEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectAssignment_User_Editor");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectAssignments)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_SI_ProjectAssignment_Projects_Project");

            entity.HasOne(d => d.TaskGroup).WithMany(p => p.ProjectAssignmentTaskGroups)
                .HasForeignKey(d => d.TaskGroupId)
                .HasConstraintName("FK_SI_ProjectAssignment_User_TaskGroup");

            // === NEW: Task Management columns (nullable for backward compatibility) ===
            entity.Property(e => e.TaskTypeId).HasColumnName("TaskTypeID");
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.WorkPriority);
            entity.Property(e => e.WorkQueueBucket).HasDefaultValue(2);

            // === Smart Tasks P1: Hierarchy ===
            entity.Property(e => e.ParentAssignmentId).HasColumnName("ParentAssignmentID");
            entity.Property(e => e.IsRequiredForParentCompletion).HasDefaultValue(true);
            entity.Property(e => e.SortOrderInParent);

            entity.HasIndex(e => e.ProjectId, "IX_ProjectAssignment_ProjectID");
            // Index on TaskTypeId
            entity.HasIndex(e => e.TaskTypeId, "IX_ProjectAssignment_TaskTypeID");

            // Index on StatusId
            entity.HasIndex(e => e.StatusId, "IX_ProjectAssignment_StatusID");

            // Index on ParentAssignmentId (helps both FK and child lookups)
            entity.HasIndex(e => e.ParentAssignmentId, "IX_ProjectAssignment_ParentAssignmentID");

            // Filtered unique index: One OPEN task per (Project, Employee, TaskType, Parent).
            // "Open" is approximated by WorkPriority IS NOT NULL — closed tasks always clear WorkPriority,
            // so this filter lets the same combination be re-created after the previous task is closed.
            entity.HasIndex(e => new { e.ProjectId, e.AssignedToId, e.TaskTypeId, e.ParentAssignmentId },
                    "IX_ProjectAssignment_UniqueOpenTask")
                .IsUnique()
                .HasFilter("[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL AND [WorkPriority] IS NOT NULL");

            // FK to TaskType (no action on delete)
            entity.HasOne(d => d.TaskType)
                .WithMany(p => p.ProjectAssignments)
                .HasForeignKey(d => d.TaskTypeId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignment_TaskType");

            // FK to ProjectAssignmentStatus (no action on delete)
            entity.HasOne(d => d.AssignmentStatus)
                .WithMany(p => p.ProjectAssignments)
                .HasForeignKey(d => d.StatusId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignment_AssignmentStatus");

            // Self-FK for parent/child hierarchy (Restrict — never cascade-delete a tree)
            entity.HasOne(d => d.ParentAssignment)
                .WithMany(p => p.ChildAssignments)
                .HasForeignKey(d => d.ParentAssignmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ProjectAssignment_ParentAssignment");
        });

        modelBuilder.Entity<ProjectBid>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("ProjectBid");

            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.SumBidValue).HasColumnType("money");
        });

        modelBuilder.Entity<ProjectBill>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("ProjectBill");

            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.SumApprovValue).HasColumnType("money");
            entity.Property(e => e.SumBillValue).HasColumnType("money");
            entity.Property(e => e.SumPaymentValue).HasColumnType("money");
        });

        modelBuilder.Entity<ProjectContract>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("ProjectContract");

            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.SumContractValue).HasColumnType("money");
        });

        modelBuilder.Entity<ProjectDeffContract>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("ProjectDeffContract");

            entity.Property(e => e.DffBilApprov).HasColumnType("money");
            entity.Property(e => e.DffBilPayment).HasColumnType("money");
            entity.Property(e => e.ProjectsId).HasColumnName("ProjectsID");
            entity.Property(e => e.SumApprovValue).HasColumnType("money");
            entity.Property(e => e.SumBillValue).HasColumnType("money");
            entity.Property(e => e.SumPaymentValue).HasColumnType("money");
        });

        modelBuilder.Entity<ProjectFile>(entity =>
        {
            entity.ToTable("ProjectFile", tb =>
                {
                    tb.HasTrigger("ProjectFile_Author");
                    tb.HasTrigger("ProjectFile_Created");
                    tb.HasTrigger("ProjectFile_Editor");
                    tb.HasTrigger("ProjectFile_Modified");
                });

            entity.HasIndex(e => new { e.Number, e.TypeProjId }, "uc_Number_TypeProjID").IsUnique();

            entity.HasIndex(e => e.Title, "uc_Title").IsUnique();

            entity.HasIndex(e => e.Code, "ux_ProjectFile_Code")
                .IsUnique()
                .HasFilter("[Code] IS NOT NULL");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.Des)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("des");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Folderid).HasColumnName("FOLDERID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.TemplateLocation)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.TypeProjId).HasColumnName("TypeProjID");
            entity.Property(e => e.Typefile)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("TYPEFILE");
            entity.Property(e => e.IsRequired)
                .HasDefaultValue(false);

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectFileAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectFile_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectFileEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectFile_User_Editor");

            entity.HasOne(d => d.Folder).WithMany(p => p.ProjectFiles)
                .HasForeignKey(d => d.Folderid)
                .HasConstraintName("FK_SI_ProjectFile_ProjectFolder_FOLDER");

            entity.HasOne(d => d.TypeProj).WithMany(p => p.ProjectFiles)
                .HasForeignKey(d => d.TypeProjId)
                .HasConstraintName("FK_SI_ProjectFile_JobType_TypeProj");
        });

        modelBuilder.Entity<ProjectFileRef>(entity =>
        {
            entity.ToTable("ProjectFileRef", tb =>
                {
                    tb.HasTrigger("ProjectFileRef_Author");
                    tb.HasTrigger("ProjectFileRef_Created");
                    tb.HasTrigger("ProjectFileRef_Editor");
                    tb.HasTrigger("ProjectFileRef_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.FileId).HasColumnName("FileID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.XrefId).HasColumnName("XRefID");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectFileRefAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectFileRef_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectFileRefEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectFileRef_User_Editor");

            entity.HasOne(d => d.File).WithMany(p => p.ProjectFileRefFiles)
                .HasForeignKey(d => d.FileId)
                .HasConstraintName("FK_SI_ProjectFileRef_ProjectFile_File");

            entity.HasOne(d => d.Xref).WithMany(p => p.ProjectFileRefXrefs)
                .HasForeignKey(d => d.XrefId)
                .HasConstraintName("FK_SI_ProjectFileRef_ProjectFile_XRef");
        });

        modelBuilder.Entity<ProjectFolder>(entity =>
        {
            entity.ToTable("ProjectFolder", tb =>
                {
                    tb.HasTrigger("ProjectFolder_Author");
                    tb.HasTrigger("ProjectFolder_Created");
                    tb.HasTrigger("ProjectFolder_Editor");
                    tb.HasTrigger("ProjectFolder_Modified");
                });

            entity.HasIndex(e => e.Title, "ProjectFolder_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Infolderid).HasColumnName("INFOLDERID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectFolderAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectFolder_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectFolderEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectFolder_User_Editor");

            entity.HasOne(d => d.Infolder).WithMany(p => p.InverseInfolder)
                .HasForeignKey(d => d.Infolderid)
                .HasConstraintName("FK_SI_ProjectFolder_ProjectFolder_INFOLDER");
        });

        modelBuilder.Entity<ProjectPlanner>(entity =>
        {
            entity.ToTable("ProjectPlanner", tb =>
                {
                    tb.HasTrigger("ProjectPlanner_Author");
                    tb.HasTrigger("ProjectPlanner_Created");
                    tb.HasTrigger("ProjectPlanner_Editor");
                    tb.HasTrigger("ProjectPlanner_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.ContactsId).HasColumnName("ContactsID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.ProjctId).HasColumnName("projctID");
            entity.Property(e => e.RoleId).HasColumnName("RoleID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectPlannerAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectPlanner_User_Author");

            entity.HasOne(d => d.Contacts).WithMany(p => p.ProjectPlanners)
                .HasForeignKey(d => d.ContactsId)
                .HasConstraintName("FK_SI_ProjectPlanner_Contacts_Contacts");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectPlannerEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectPlanner_User_Editor");

            entity.HasOne(d => d.Projct).WithMany(p => p.ProjectPlanners)
                .HasForeignKey(d => d.ProjctId)
                .HasConstraintName("FK_SI_ProjectPlanner_Projects_projct");

            entity.HasOne(d => d.Role).WithMany(p => p.ProjectPlanners)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_SI_ProjectPlanner_jobTitle_Role");
        });

        modelBuilder.Entity<ProjectStatus>(entity =>
        {
            entity.ToTable("ProjectStatus", tb =>
                {
                    tb.HasTrigger("ProjectStatus_Author");
                    tb.HasTrigger("ProjectStatus_Created");
                    tb.HasTrigger("ProjectStatus_Editor");
                    tb.HasTrigger("ProjectStatus_Modified");
                });

            entity.HasIndex(e => e.Title, "ProjectStatus_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.ProjectStatusAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ProjectStatus_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ProjectStatusEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ProjectStatus_User_Editor");
        });

        modelBuilder.Entity<Property>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("Properties_Author");
                    tb.HasTrigger("Properties_Created");
                    tb.HasTrigger("Properties_Editor");
                    tb.HasTrigger("Properties_Modified");
                });

            entity.HasIndex(e => e.Title, "Properties_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.DescriptionX0020He)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("Description_x0020_HE");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.LayersId).HasColumnName("LayersID");
            entity.Property(e => e.Linetype)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.MifratId).HasColumnName("MifratID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.PlotStyleName)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.PropertyAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_Properties_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.PropertyEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_Properties_User_Editor");

            entity.HasOne(d => d.Layers).WithMany(p => p.Properties)
                .HasForeignKey(d => d.LayersId)
                .HasConstraintName("FK_SI_Properties_Layers_Layers");

            entity.HasOne(d => d.Mifrat).WithMany(p => p.Properties)
                .HasForeignKey(d => d.MifratId)
                .HasConstraintName("FK_SI_Properties_Mifrat_Mifrat");
        });

        modelBuilder.Entity<ServiceProvider>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("ServiceProviders_Author");
                    tb.HasTrigger("ServiceProviders_Created");
                    tb.HasTrigger("ServiceProviders_Editor");
                    tb.HasTrigger("ServiceProviders_Modified");
                });

            entity.HasIndex(e => e.Title, "ServiceProviders_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.ServiceProviderAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_ServiceProviders_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.ServiceProviderEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_ServiceProviders_User_Editor");
        });

        modelBuilder.Entity<Siuser>(entity =>
        {
            entity.ToTable("SIUser");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.LoginName)
                .HasMaxLength(255)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Notes)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Sid)
                .HasMaxLength(255)
                .IsUnicode(false)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("sid");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.Role)
                .HasDefaultValue(AppUserRole.Employee)
                .HasSentinel(AppUserRole.Employee);

            entity.HasIndex(e => e.MasterPlanEmployeeId)
                .IsUnique()
                .HasFilter("[MasterPlanEmployeeId] IS NOT NULL");

        });

        modelBuilder.Entity<TabaDatum>(entity =>
        {
            entity.ToTable(tb =>
                {
                    tb.HasTrigger("TabaData_Author");
                    tb.HasTrigger("TabaData_Created");
                    tb.HasTrigger("TabaData_Editor");
                    tb.HasTrigger("TabaData_Modified");
                });

            entity.HasIndex(e => e.Title, "TabaData_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Grop)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.LayerColorAa).HasColumnName("LayerColorAA");
            entity.Property(e => e.LayerColorBa).HasColumnName("LayerColorBA");
            entity.Property(e => e.LayerColorBb).HasColumnName("LayerColorBB");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Seyf)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.TabaDatumAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_TabaData_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.TabaDatumEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_TabaData_User_Editor");
        });

        modelBuilder.Entity<TypeOfProjectInProject>(entity =>
        {
            entity.ToTable("TypeOfProjectInProject", tb =>
                {
                    tb.HasTrigger("TypeOfProjectInProject_Author");
                    tb.HasTrigger("TypeOfProjectInProject_Created");
                    tb.HasTrigger("TypeOfProjectInProject_Editor");
                    tb.HasTrigger("TypeOfProjectInProject_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AdminWorkerId).HasColumnName("AdminWorkerID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.ProjectId).HasColumnName("ProjectID");
            entity.Property(e => e.ProjectTypeId).HasColumnName("ProjectTypeID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.AdminWorker).WithMany(p => p.TypeOfProjectInProjectAdminWorkers)
                .HasForeignKey(d => d.AdminWorkerId)
                .HasConstraintName("FK_SI_TypeOfProjectInProject_User_AdminWorker");

            entity.HasOne(d => d.Author).WithMany(p => p.TypeOfProjectInProjectAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_TypeOfProjectInProject_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.TypeOfProjectInProjectEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_TypeOfProjectInProject_User_Editor");

            entity.HasOne(d => d.Project).WithMany(p => p.TypeOfProjectInProjects)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_SI_TypeOfProjectInProject_Projects_Project");

            entity.HasOne(d => d.ProjectType).WithMany(p => p.TypeOfProjectInProjects)
                .HasForeignKey(d => d.ProjectTypeId)
                .HasConstraintName("FK_SI_TypeOfProjectInProject_JobType_ProjectType");
        });

        modelBuilder.Entity<WeekWork>(entity =>
        {
            entity.ToTable("WeekWork", tb =>
                {
                    tb.HasTrigger("WeekWork_Author");
                    tb.HasTrigger("WeekWork_Created");
                    tb.HasTrigger("WeekWork_Editor");
                    tb.HasTrigger("WeekWork_Modified");
                });

            entity.HasIndex(e => e.Title, "WeekWork_TitleIndex").IsUnique();

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.JobStatus)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.ProjectId).HasColumnName("ProjectID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Week).HasColumnType("datetime");
            entity.Property(e => e.WorkerId).HasColumnName("workerID");

            entity.HasOne(d => d.Author).WithMany(p => p.WeekWorkAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_WeekWork_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.WeekWorkEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_WeekWork_User_Editor");

            entity.HasOne(d => d.Project).WithMany(p => p.WeekWorks)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_SI_WeekWork_Projects_Project");

            entity.HasOne(d => d.Worker).WithMany(p => p.WeekWorkWorkers)
                .HasForeignKey(d => d.WorkerId)
                .HasConstraintName("FK_SI_WeekWork_User_worker");
        });

        modelBuilder.Entity<WorkHour>(entity =>
        {
            entity.ToTable("WorkHour", tb =>
                {
                    tb.HasTrigger("WorkHour_Author");
                    tb.HasTrigger("WorkHour_Created");
                    tb.HasTrigger("WorkHour_Editor");
                    tb.HasTrigger("WorkHour_Modified");
                });

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AuthorId).HasColumnName("AuthorID");
            entity.Property(e => e.Category)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Created).HasColumnType("datetime");
            entity.Property(e => e.Description).UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.EditorId).HasColumnName("EditorID");
            entity.Property(e => e.EndDate).HasColumnType("datetime");
            entity.Property(e => e.EventDate).HasColumnType("datetime");
            entity.Property(e => e.FAllDayEvent).HasColumnName("fAllDayEvent");
            entity.Property(e => e.FRecurrence)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS")
                .HasColumnName("fRecurrence");
            entity.Property(e => e.Facilities)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.FreeBusy)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Location)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Modified).HasColumnType("datetime");
            entity.Property(e => e.Overbook)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.ParticipantsPicker)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.PayByHomer).HasColumnName("payByHomer");
            entity.Property(e => e.ProjectId).HasColumnName("ProjectID");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.WorkspaceLink)
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");

            entity.HasOne(d => d.Author).WithMany(p => p.WorkHourAuthors)
                .HasForeignKey(d => d.AuthorId)
                .HasConstraintName("FK_SI_WorkHour_User_Author");

            entity.HasOne(d => d.Editor).WithMany(p => p.WorkHourEditors)
                .HasForeignKey(d => d.EditorId)
                .HasConstraintName("FK_SI_WorkHour_User_Editor");

            entity.HasOne(d => d.Project).WithMany(p => p.WorkHours)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_SI_WorkHour_Projects_Project");
        });

        modelBuilder.Entity<ThreadStatusMapping>(entity =>
        {
            // Stage D: surrogate technical primary key (identity). Business
            // identity is enforced via the UNIQUE index on ThreadUniqueId.
            entity.HasKey(e => e.Id);

            // Table name must match what exists in SQL Server
            entity.ToTable("ThreadStatusMapping");

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            // Runtime Gmail adapter mirror — nullable; NOT a business key.
            // Gmail thread ids are up to 16 chars (hex); 100 is kept for safety.
            entity.Property(e => e.ThreadId)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.Property(e => e.Status)
                .HasConversion<int>();

            entity.Property(e => e.LastUpdated)
                .HasColumnType("datetime2");

            entity.Property(e => e.BimFolderId)
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.Property(e => e.GmailLabelId)
                .HasMaxLength(100)
                .IsUnicode(false);

            // Stage D: required, unique business key.
            entity.Property(e => e.ThreadUniqueId)
                .IsRequired()
                .HasMaxLength(255)
                .IsUnicode(false);

            // 🛑 STRICT FOREIGN KEY RELATIONSHIP
            // Every ThreadStatusMapping MUST belong to a valid Project
            // Cascade Delete: When a Project is deleted, all its thread mappings are automatically removed
            entity.HasOne(d => d.Project)
                .WithMany()
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade)  // Auto-delete mappings when Project is deleted
                .IsRequired()                       // Enforces non-null constraint
                .HasConstraintName("FK_ThreadStatusMapping_Projects");

            entity.HasIndex(e => e.ProjectId, "IX_ThreadStatusMapping_ProjectId");
            entity.HasIndex(e => e.Status, "IX_ThreadStatusMapping_Status");
            entity.HasIndex(e => e.LastUpdated, "IX_ThreadStatusMapping_LastUpdated");

            // Stage D: plain UNIQUE index on the business key (not filtered).
            entity.HasIndex(e => e.ThreadUniqueId, "UQ_ThreadStatusMapping_ThreadUniqueId")
                .IsUnique();

            // Adapter index for fast Gmail-thread-id reverse lookup (non-unique,
            // filtered to NOT NULL because ThreadId is now optional).
            entity.HasIndex(e => e.ThreadId, "IX_ThreadStatusMapping_ThreadId")
                .HasFilter("[ThreadId] IS NOT NULL");
        });

        // ========================================
        // === Task Management Entity Configurations ===
        // ========================================

        modelBuilder.Entity<TaskType>(entity =>
        {
            entity.ToTable("TaskType");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValueSql("CAST(NEWID() AS NVARCHAR(50))");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0);
            entity.Property(e => e.DefaultWorkQueueBucket);

            entity.HasIndex(e => e.Code, "IX_TaskType_Code").IsUnique();
            entity.HasIndex(e => e.Name, "IX_TaskType_Name").IsUnique();
        });

        modelBuilder.Entity<ProjectAssignmentStatus>(entity =>
        {
            entity.ToTable("ProjectAssignmentStatus");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0);
            entity.Property(e => e.DefaultColorHex)
                .HasMaxLength(9);

            entity.HasIndex(e => e.Name, "IX_ProjectAssignmentStatus_Name").IsUnique();
        });

        modelBuilder.Entity<ProjectAssignmentEvent>(entity =>
        {
            entity.ToTable("ProjectAssignmentEvent");

            // Ignore computed property - not stored in DB
            entity.Ignore(e => e.LocalCreatedDate);

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ProjectAssignmentId).HasColumnName("ProjectAssignmentID");
            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(100)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.OldStatusId).HasColumnName("OldStatusID");
            entity.Property(e => e.NewStatusId).HasColumnName("NewStatusID");
            entity.Property(e => e.ContactId).HasColumnName("ContactID");
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.ExternalReferenceText)
                .HasMaxLength(500)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.Note)
                .UseCollation("Hebrew_100_CI_AS");
            entity.Property(e => e.EmailThreadId)
                .HasMaxLength(100)
                .IsUnicode(false);  // Simple string, no FK
            entity.Property(e => e.CreatedByUserId).HasColumnName("CreatedByUserID");
            entity.Property(e => e.CreatedDate)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.ProjectAssignmentId, "IX_ProjectAssignmentEvent_ProjectAssignmentID");
            entity.HasIndex(e => e.CreatedDate, "IX_ProjectAssignmentEvent_CreatedDate");

            // FK to ProjectAssignment (cascade delete - when task deleted, events are deleted)
            entity.HasOne(d => d.ProjectAssignment)
                .WithMany(p => p.ProjectAssignmentEvents)
                .HasForeignKey(d => d.ProjectAssignmentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProjectAssignmentEvent_ProjectAssignment");

            // FK to OldStatus (no action on delete)
            entity.HasOne(d => d.OldStatus)
                .WithMany(p => p.ProjectAssignmentEventsOldStatus)
                .HasForeignKey(d => d.OldStatusId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_OldStatus");

            // FK to NewStatus (no action on delete)
            entity.HasOne(d => d.NewStatus)
                .WithMany(p => p.ProjectAssignmentEventsNewStatus)
                .HasForeignKey(d => d.NewStatusId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_NewStatus");

            // FK to Contact (no action on delete)
            entity.HasOne(d => d.Contact)
                .WithMany(p => p.ProjectAssignmentEvents)
                .HasForeignKey(d => d.ContactId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_Contact");

            // FK to Company (no action on delete)
            entity.HasOne(d => d.Company)
                .WithMany(p => p.ProjectAssignmentEvents)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_Company");

            // FK to CreatedByUser (no action on delete)
            entity.HasOne(d => d.CreatedByUser)
                .WithMany(p => p.ProjectAssignmentEventsCreatedBy)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_CreatedByUser");

            // FK to TaskLink — optional proof/evidence for this event (no action on delete)
            entity.Property(e => e.TaskLinkId).HasColumnName("TaskLinkID");
            entity.HasOne(d => d.TaskLink)
                .WithMany(p => p.ProofEvents)
                .HasForeignKey(d => d.TaskLinkId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProjectAssignmentEvent_TaskLink");
        });

        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.ToTable("UserSetting");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.SiuserId).HasColumnName("SIUserID");
            entity.Property(e => e.AutoOpenTasksPanelAfterFiling)
                .HasDefaultValue(true);

            entity.Property(e => e.GmailMailScope)
                .HasMaxLength(32);
            entity.Property(e => e.GmailMailCategory)
                .HasMaxLength(32);
            entity.Property(e => e.GmailUnreadOnly)
                .HasDefaultValue(false);

            // Unique index on SIUserID (one setting per user)
            entity.HasIndex(e => e.SiuserId, "IX_UserSetting_SIUserID").IsUnique();

            // FK to Siuser (cascade delete)
            entity.HasOne(d => d.Siuser)
                .WithOne(p => p.UserSetting)
                .HasForeignKey<UserSetting>(d => d.SiuserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserSetting_SIUser");
        });

        // === NEW: ProjectType-TaskType mapping (which TaskTypes are allowed per ProjectType) ===
        modelBuilder.Entity<ProjectTypeTaskType>(entity =>
        {
            entity.ToTable("ProjectTypeTaskType");

            // Composite primary key
            entity.HasKey(e => new { e.ProjectTypeId, e.TaskTypeId });

            entity.Property(e => e.ProjectTypeId).HasColumnName("ProjectTypeID");
            entity.Property(e => e.TaskTypeId).HasColumnName("TaskTypeID");

            // Unique index (redundant with PK but explicit)
            entity.HasIndex(e => new { e.ProjectTypeId, e.TaskTypeId }, "IX_ProjectTypeTaskType_Unique").IsUnique();

            // FK to JobType (ProjectType)
            entity.HasOne(d => d.ProjectType)
                .WithMany(p => p.AllowedTaskTypes)
                .HasForeignKey(d => d.ProjectTypeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProjectTypeTaskType_JobType");

            // FK to TaskType
            entity.HasOne(d => d.TaskType)
                .WithMany(p => p.AllowedForProjectTypes)
                .HasForeignKey(d => d.TaskTypeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProjectTypeTaskType_TaskType");
        });

        // === NEW: ProjectType-Status mapping (which Statuses are allowed per ProjectType) ===
        modelBuilder.Entity<ProjectTypeStatus>(entity =>
        {
            entity.ToTable("ProjectTypeStatus");

            // Composite primary key
            entity.HasKey(e => new { e.ProjectTypeId, e.StatusId });

            entity.Property(e => e.ProjectTypeId).HasColumnName("ProjectTypeID");
            entity.Property(e => e.StatusId).HasColumnName("StatusID");

            // Unique index (redundant with PK but explicit)
            entity.HasIndex(e => new { e.ProjectTypeId, e.StatusId }, "IX_ProjectTypeStatus_Unique").IsUnique();

            // FK to JobType (ProjectType)
            entity.HasOne(d => d.ProjectType)
                .WithMany(p => p.AllowedStatuses)
                .HasForeignKey(d => d.ProjectTypeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProjectTypeStatus_JobType");

            // FK to ProjectAssignmentStatus
            entity.HasOne(d => d.Status)
                .WithMany(p => p.AllowedForProjectTypes)
                .HasForeignKey(d => d.StatusId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProjectTypeStatus_ProjectAssignmentStatus");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
