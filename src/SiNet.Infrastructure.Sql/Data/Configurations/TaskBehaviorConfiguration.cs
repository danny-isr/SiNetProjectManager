using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public class TaskBehaviorDefinitionConfiguration : IEntityTypeConfiguration<TaskBehaviorDefinition>
{
    public void Configure(EntityTypeBuilder<TaskBehaviorDefinition> builder)
    {
        builder.ToTable("TaskBehaviorDefinitions");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("IX_TaskBehaviorDefinition_Code");

        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);

        // Smart Tasks P2: aggregation policy for parent/child & work-target rollup
        builder.Property(e => e.AggregationMode)
            .HasConversion<int>()
            .HasDefaultValue(TaskAggregationMode.AllRequired);

        builder.HasOne(e => e.TaskType)
            .WithOne(t => t.BehaviorDefinition)
            .HasForeignKey<TaskBehaviorDefinition>(e => e.TaskTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.TriggerRules)
            .WithOne(e => e.BehaviorDefinition)
            .HasForeignKey(e => e.BehaviorDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.CompletionRules)
            .WithOne(e => e.BehaviorDefinition)
            .HasForeignKey(e => e.BehaviorDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TaskTriggerRuleConfiguration : IEntityTypeConfiguration<TaskTriggerRule>
{
    public void Configure(EntityTypeBuilder<TaskTriggerRule> builder)
    {
        builder.ToTable("TaskTriggerRules");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ConditionJson).HasMaxLength(1000);
        builder.Property(e => e.Description).HasMaxLength(300);
    }
}

public class TaskCompletionRuleConfiguration : IEntityTypeConfiguration<TaskCompletionRule>
{
    public void Configure(EntityTypeBuilder<TaskCompletionRule> builder)
    {
        builder.ToTable("TaskCompletionRules");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ConditionJson).HasMaxLength(1000);
        builder.Property(e => e.Description).HasMaxLength(300);

        builder.HasOne(e => e.ResultingStatus)
            .WithMany()
            .HasForeignKey(e => e.ResultingStatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
