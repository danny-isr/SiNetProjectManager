using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the Workflow domain:
/// WorkflowDefinition, WorkflowStageDefinition, WorkflowTransitionRule,
/// WorkflowInstance, WorkflowStageTransition.
/// </summary>
public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinition");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        // Unique code per definition
        builder.HasIndex(e => e.Code, "IX_WorkflowDefinition_Code")
            .IsUnique();

        // Unique name per definition (user-facing, auto-generated Code mirrors this)
        builder.HasIndex(e => e.Name, "IX_WorkflowDefinition_Name")
            .IsUnique();
    }
}

public class WorkflowStageDefinitionConfiguration : IEntityTypeConfiguration<WorkflowStageDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowStageDefinition> builder)
    {
        builder.ToTable("WorkflowStageDefinition");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("WorkflowDefinitionID")
            .IsRequired();

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.SortOrder).IsRequired();

        // ── Visual Designer fields ──
        builder.Property(e => e.NodeType)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Stage");

        builder.Property(e => e.Color)
            .HasMaxLength(20);

        // -- Sub-Workflow fields --
        builder.Property(e => e.SubWorkflowDefinitionId)
            .HasColumnName("SubWorkflowDefinitionID");

        builder.Property(e => e.SubWorkflowWaitMode)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WorkflowSubWorkflowWaitMode.WaitForCompletion);

        // Unique code within a definition
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Code }, "IX_WorkflowStageDefinition_DefCode")
            .IsUnique();

        // Unique name within a definition (user-facing)
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Name }, "IX_WorkflowStageDefinition_DefName")
            .IsUnique();

        // Fast lookup by definition + sort order
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.SortOrder }, "IX_WorkflowStageDefinition_DefSort");

        // FK to WorkflowDefinition
        builder.HasOne(e => e.WorkflowDefinition)
            .WithMany(d => d.Stages)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowStageDefinition_Definition");

        // FK to SubWorkflowDefinition (optional, self-referencing WorkflowDefinition)
        builder.HasOne(e => e.SubWorkflowDefinition)
            .WithMany()
            .HasForeignKey(e => e.SubWorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_WorkflowStageDefinition_SubWorkflow");

        // FK to UserGroup (optional — which group is responsible for this stage)
        builder.Property(e => e.AssignedGroupId)
            .HasColumnName("AssignedGroupID");

        builder.HasOne(e => e.AssignedGroup)
            .WithMany()
            .HasForeignKey(e => e.AssignedGroupId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_WorkflowStageDefinition_AssignedGroup");
    }
}

public class WorkflowTransitionRuleConfiguration : IEntityTypeConfiguration<WorkflowTransitionRule>
{
    public void Configure(EntityTypeBuilder<WorkflowTransitionRule> builder)
    {
        builder.ToTable("WorkflowTransitionRule");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("WorkflowDefinitionID")
            .IsRequired();

        builder.Property(e => e.FromStageId)
            .HasColumnName("FromStageID")
            .IsRequired();

        builder.Property(e => e.ToStageId)
            .HasColumnName("ToStageID")
            .IsRequired();

        builder.Property(e => e.Name)
            .HasMaxLength(200);

        // -- Trigger / Condition / Evaluation --
        builder.Property(e => e.TriggerType)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WorkflowTransitionTriggerType.Manual);

        builder.Property(e => e.ConditionType)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WorkflowTransitionConditionType.Always);

        builder.Property(e => e.ConditionJson)
            .HasMaxLength(2000);

        builder.Property(e => e.ConditionHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength(false);

        builder.Property(e => e.EvaluationMode)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WorkflowEvaluationMode.Manual)
            .HasSentinel(WorkflowEvaluationMode.Manual);

        // ── Visual Designer fields ──
        builder.Property(e => e.Condition)
            .HasMaxLength(500);

        builder.Property(e => e.Label)
            .HasMaxLength(100);

        builder.Property(e => e.RoutePointsJson)
            .HasMaxLength(4000);

        // Unique transition per definition.
        // The previous index (WorkflowDefinitionId, FromStageId, ToStageId) was too
        // restrictive — it forbade multiple legitimate transitions between the same
        // pair of stages (e.g. REV.Close → REV.Close differing only by TaskResult,
        // or REV.PoliceApproved → REV.Close with Manual vs ActionCompleted triggers).
        // The new key adds TriggerType + ConditionType + ConditionHash so distinct
        // condition payloads coexist, while genuine duplicates are still blocked.
        builder.HasIndex(e => new
        {
            e.WorkflowDefinitionId,
            e.FromStageId,
            e.ToStageId,
            e.TriggerType,
            e.ConditionType,
            e.ConditionHash,
        }, "IX_WorkflowTransitionRule_Unique").IsUnique();

        // FK to WorkflowDefinition
        builder.HasOne(e => e.WorkflowDefinition)
            .WithMany(d => d.TransitionRules)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowTransitionRule_Definition");

        // FK to FromStage (no cascade — delete rule via definition cascade)
        builder.HasOne(e => e.FromStage)
            .WithMany(s => s.TransitionRulesFrom)
            .HasForeignKey(e => e.FromStageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowTransitionRule_FromStage");

        // FK to ToStage (no cascade)
        builder.HasOne(e => e.ToStage)
            .WithMany(s => s.TransitionRulesTo)
            .HasForeignKey(e => e.ToStageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowTransitionRule_ToStage");
    }
}

public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstance");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("WorkflowDefinitionID")
            .IsRequired();

        builder.Property(e => e.ProjectId)
            .HasColumnName("ProjectID")
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.CurrentStageId)
            .HasColumnName("CurrentStageID");

        builder.Property(e => e.TriggerType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.TriggerEntityId)
            .HasColumnName("TriggerEntityID");

        builder.Property(e => e.ParentWorkflowInstanceId)
            .HasColumnName("ParentWorkflowInstanceID");

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("CreatedByUserID")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc).IsRequired();

        builder.Property(e => e.Notes)
            .HasMaxLength(2000);

        builder.Property(e => e.IsProjectBound)
            .IsRequired()
            .HasDefaultValue(true);

        // ═══ Indexes ═══

        // Fast lookup: all workflows for a project
        builder.HasIndex(e => e.ProjectId, "IX_WorkflowInstance_Project");

        // Fast lookup: active workflows
        builder.HasIndex(e => e.Status, "IX_WorkflowInstance_Status");

        // Composite: active workflows per project
        builder.HasIndex(e => new { e.ProjectId, e.Status }, "IX_WorkflowInstance_ProjectStatus");

        // Fast lookup: child sub-workflows of a parent instance
        builder.HasIndex(e => e.ParentWorkflowInstanceId, "IX_WorkflowInstance_Parent");

        // ═══ Relationships ═══

        builder.HasOne(e => e.WorkflowDefinition)
            .WithMany(d => d.Instances)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_WorkflowInstance_Definition");

        builder.HasOne(e => e.Project)
            .WithMany(p => p.WorkflowInstances)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowInstance_Project");

        builder.HasOne(e => e.CurrentStage)
            .WithMany()
            .HasForeignKey(e => e.CurrentStageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowInstance_CurrentStage");

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowInstance_CreatedByUser");

        builder.HasOne(e => e.ParentWorkflowInstance)
            .WithMany(e => e.ChildWorkflowInstances)
            .HasForeignKey(e => e.ParentWorkflowInstanceId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowInstance_Parent");
    }
}

public class WorkflowStageTransitionConfiguration : IEntityTypeConfiguration<WorkflowStageTransition>
{
    public void Configure(EntityTypeBuilder<WorkflowStageTransition> builder)
    {
        builder.ToTable("WorkflowStageTransition");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.WorkflowInstanceId)
            .HasColumnName("WorkflowInstanceID")
            .IsRequired();

        builder.Property(e => e.ToStageId)
            .HasColumnName("ToStageID")
            .IsRequired();

        builder.Property(e => e.FromStageId)
            .HasColumnName("FromStageID");

        builder.Property(e => e.TransitionedByUserId)
            .HasColumnName("TransitionedByUserID")
            .IsRequired();

        builder.Property(e => e.TransitionedAtUtc).IsRequired();

        builder.Property(e => e.Notes)
            .HasMaxLength(2000);

        // ═══ Indexes ═══

        // Fast lookup: all transitions for an instance (chronological)
        builder.HasIndex(e => new { e.WorkflowInstanceId, e.TransitionedAtUtc },
            "IX_WorkflowStageTransition_InstanceTime");

        // ═══ Relationships ═══

        builder.HasOne(e => e.WorkflowInstance)
            .WithMany(i => i.StageTransitions)
            .HasForeignKey(e => e.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowStageTransition_Instance");

        builder.HasOne(e => e.ToStage)
            .WithMany(s => s.TransitionsEntered)
            .HasForeignKey(e => e.ToStageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowStageTransition_ToStage");

        builder.HasOne(e => e.TransitionedByUser)
            .WithMany()
            .HasForeignKey(e => e.TransitionedByUserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkflowStageTransition_User");
    }
}

public class WorkflowTransitionActionConfiguration : IEntityTypeConfiguration<WorkflowTransitionAction>
{
    public void Configure(EntityTypeBuilder<WorkflowTransitionAction> builder)
    {
        builder.ToTable("WorkflowTransitionAction");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.TransitionRuleId)
            .HasColumnName("TransitionRuleID")
            .IsRequired();

        builder.Property(e => e.ActionType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.ActionCode)
            .HasMaxLength(80);

        builder.Property(e => e.ConfigJson)
            .HasMaxLength(4000);

        builder.Property(e => e.SortOrder).IsRequired();

        // FK to TransitionRule
        builder.HasOne(e => e.TransitionRule)
            .WithMany(r => r.Actions)
            .HasForeignKey(e => e.TransitionRuleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowTransitionAction_Rule");

        // Fast lookup by rule + sort order
        builder.HasIndex(e => new { e.TransitionRuleId, e.SortOrder },
            "IX_WorkflowTransitionAction_RuleSort");
    }
}

public class WorkflowStartTriggerConfiguration : IEntityTypeConfiguration<WorkflowStartTrigger>
{
    public void Configure(EntityTypeBuilder<WorkflowStartTrigger> builder)
    {
        builder.ToTable("WorkflowStartTrigger");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");

        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("WorkflowDefinitionID")
            .IsRequired();

        builder.Property(e => e.TriggerSource)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(WorkflowStartTriggerSource.ManualStart);

        builder.Property(e => e.PropertiesJson)
            .HasMaxLength(4000);

        builder.Property(e => e.ParameterMappingJson)
            .HasMaxLength(4000);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.SortOrder).IsRequired();

        // Ignore the C# Properties object — backed by PropertiesJson
        builder.Ignore(e => e.Properties);

        // FK to WorkflowDefinition
        builder.HasOne(e => e.WorkflowDefinition)
            .WithMany(d => d.StartTriggers)
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkflowStartTrigger_Definition");

        // Fast lookup by definition + sort order
        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.SortOrder },
            "IX_WorkflowStartTrigger_DefSort");

        // Fast lookup by trigger source (when evaluating events)
        builder.HasIndex(e => new { e.TriggerSource, e.IsActive },
            "IX_WorkflowStartTrigger_SourceActive");
    }
}
