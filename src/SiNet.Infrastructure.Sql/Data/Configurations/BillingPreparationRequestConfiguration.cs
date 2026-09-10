using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public sealed class BillingPreparationRequestConfiguration : IEntityTypeConfiguration<BillingPreparationRequest>
{
    public void Configure(EntityTypeBuilder<BillingPreparationRequest> builder)
    {
        builder.ToTable("BillingPreparationRequest");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.MasterPlanProjectId).HasColumnName("MasterPlanProjectID");
        builder.Property(e => e.SiNetProjectId).HasColumnName("SiNetProjectID");
        builder.Property(e => e.ProjectNumber).HasMaxLength(32);
        builder.Property(e => e.ProjectName).HasMaxLength(400);
        builder.Property(e => e.CustomerName).HasMaxLength(400);
        builder.Property(e => e.CreatedByLogin).HasMaxLength(200);
        builder.Property(e => e.ApprovedByLogin).HasMaxLength(200);
        builder.Property(e => e.ManualOverrideReason).HasMaxLength(1000);
        builder.HasIndex(e => e.MasterPlanProjectId, "IX_BillingPreparationRequest_MasterPlanProjectID");
        builder.HasIndex(e => e.TaskId, "IX_BillingPreparationRequest_TaskID");
        builder.HasIndex(e => e.Status, "IX_BillingPreparationRequest_Status");
        builder.HasMany(e => e.Stages)
            .WithOne(e => e.Request)
            .HasForeignKey(e => e.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Hours)
            .WithOne(e => e.Request)
            .HasForeignKey(e => e.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BillingPreparationStageLineConfiguration : IEntityTypeConfiguration<BillingPreparationStageLine>
{
    public void Configure(EntityTypeBuilder<BillingPreparationStageLine> builder)
    {
        builder.ToTable("BillingPreparationStageLine");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.RequestId).HasColumnName("RequestID");
        builder.Property(e => e.MasterPlanStageId).HasColumnName("MasterPlanStageID");
        builder.Property(e => e.MasterPlanSubContractId).HasColumnName("MasterPlanSubContractID");
        builder.Property(e => e.StageName).IsRequired().HasMaxLength(400);
        builder.Property(e => e.SubContractName).IsRequired().HasMaxLength(400);
        builder.Property(e => e.StageWeightWithinSubContract).HasColumnType("decimal(18,6)");
        builder.Property(e => e.ObservedCumulativeProgress).HasColumnType("decimal(18,6)");
        builder.Property(e => e.TargetCumulativeProgress).HasColumnType("decimal(18,6)");
        builder.Property(e => e.RequestedDelta).HasColumnType("decimal(18,6)");
        builder.Property(e => e.ConfirmationNote).HasMaxLength(1000);
        builder.HasIndex(e => new { e.RequestId, e.MasterPlanStageId }, "UX_BillingPreparationStageLine_Request_Stage")
            .IsUnique();
    }
}

public sealed class BillingPreparationHoursLineConfiguration : IEntityTypeConfiguration<BillingPreparationHoursLine>
{
    public void Configure(EntityTypeBuilder<BillingPreparationHoursLine> builder)
    {
        builder.ToTable("BillingPreparationHoursLine");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.RequestId).HasColumnName("RequestID");
        builder.Property(e => e.MasterPlanSubContractId).HasColumnName("MasterPlanSubContractID");
        builder.Property(e => e.SubContractName).IsRequired().HasMaxLength(400);
        builder.Property(e => e.TotalHours).HasColumnType("decimal(18,4)");
        builder.Property(e => e.OverlappingHourReportIds).HasMaxLength(2000);
        builder.Property(e => e.ConfirmationNote).HasMaxLength(1000);
        builder.HasMany(e => e.Reports)
            .WithOne(e => e.HoursLine)
            .HasForeignKey(e => e.HoursLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BillingPreparationHoursReportConfiguration : IEntityTypeConfiguration<BillingPreparationHoursReport>
{
    public void Configure(EntityTypeBuilder<BillingPreparationHoursReport> builder)
    {
        builder.ToTable("BillingPreparationHoursReport");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.HoursLineId).HasColumnName("HoursLineID");
        builder.Property(e => e.HoursReportId).HasColumnName("HoursReportID");
        builder.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
        builder.Property(e => e.EmployeeName).HasMaxLength(200);
        builder.Property(e => e.Hours).HasColumnType("decimal(18,4)");
        builder.Property(e => e.SubContractId).HasColumnName("SubContractID");
        builder.Property(e => e.SubContractStepId).HasColumnName("SubContractStepID");
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.HasIndex(e => e.HoursReportId, "IX_BillingPreparationHoursReport_HoursReportID");
    }
}
