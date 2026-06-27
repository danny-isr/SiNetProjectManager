using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data.Configurations;
using SiNetSQL.Models;

namespace SiNetSQL.Data;

/// <summary>
/// Partial class extension for SiNetSQLDbContext.
/// Registers Email Inbox Ingestion and ACC Mapping entity configurations.
/// </summary>
public partial class SiNetSQLDbContext
{
    /// <summary>
    /// Called at the end of OnModelCreating to apply custom configurations.
    /// This is the hook point for adding Fluent API configurations.
    /// </summary>
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Inspection System Configurations
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new InspectionReportConfiguration());
        modelBuilder.ApplyConfiguration(new ChapterNameConfiguration());
        modelBuilder.ApplyConfiguration(new SectionNameConfiguration());
        modelBuilder.ApplyConfiguration(new ChapterConfiguration());
        modelBuilder.ApplyConfiguration(new SectionConfiguration());
        modelBuilder.ApplyConfiguration(new CommentsBankConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionNoteConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionNoteStatusConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionSeriesConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionReportDrawingConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionSeriesFileConfigConfiguration());
        modelBuilder.ApplyConfiguration(new InspectionReportReviewedFileConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Email Inbox Ingestion Configurations
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new EmailInboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new EmailInboxAttachmentConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // ACC (Autodesk Construction Cloud) Mapping Configurations
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new AccHubConfiguration());
        modelBuilder.ApplyConfiguration(new AccSystemResourceConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectAccMappingConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Sync Engine Failure Logging Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new SyncRunFailureConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // User Status Color Preference Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new UserStatusPreferenceConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Task-to-Project Status Mapping Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new TaskStatusToProjectStatusMappingConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Project Decisions System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new DecisionCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectDecisionConfiguration());
        modelBuilder.ApplyConfiguration(new DecisionHistoryConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Centralized System Settings Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new SystemSettingConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Task Linking System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new TaskLinkConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Workflow System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new WorkflowDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowStageDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTransitionRuleConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowInstanceConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowStageTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTransitionActionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowStartTriggerConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Action Permission System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new ActionPermissionConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Workflow Stage ↔ Task Mapping Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new WorkflowStageTaskConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // ProjectType ↔ WorkflowDefinition Mapping Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new ProjectTypeWorkflowDefinitionConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Task Behavior System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new TaskBehaviorDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new TaskTriggerRuleConfiguration());
        modelBuilder.ApplyConfiguration(new TaskCompletionRuleConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Project Alternative System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new ProjectAlternativeConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // User Groups System Configuration
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new UserGroupConfiguration());
        modelBuilder.ApplyConfiguration(new UserGroupMembershipConfiguration());

        // ═══════════════════════════════════════════════════════════════════════
        // Planning Workflow Taxonomy (TaskResult / ProjectTypeWorkflowStage /
        // ProjectTypeDiscipline + ProjectStatus.Code / ProjectAssignmentStatus.Code /
        // ProjectAssignment.LastTaskResultId / ProjectAssignmentEvent.TaskResultId)
        // ═══════════════════════════════════════════════════════════════════════
        modelBuilder.ApplyConfiguration(new TaskResultDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectTypeWorkflowStageConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectTypeDisciplineConfiguration());
        var planningTaxonomy = new PlanningTaxonomyExtensionsConfiguration();
        modelBuilder.ApplyConfiguration((IEntityTypeConfiguration<ProjectStatus>)planningTaxonomy);
        modelBuilder.ApplyConfiguration((IEntityTypeConfiguration<ProjectAssignmentStatus>)planningTaxonomy);
        modelBuilder.ApplyConfiguration((IEntityTypeConfiguration<ProjectAssignment>)planningTaxonomy);
        modelBuilder.ApplyConfiguration((IEntityTypeConfiguration<ProjectAssignmentEvent>)planningTaxonomy);
    }
}
