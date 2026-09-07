using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public sealed class BillingReviewDecisionConfiguration : IEntityTypeConfiguration<BillingReviewDecision>
{
    public void Configure(EntityTypeBuilder<BillingReviewDecision> builder)
    {
        builder.ToTable("BillingReviewDecision");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.ProjectId).HasColumnName("ProjectID");
        builder.Property(e => e.DecisionType)
            .IsRequired()
            .HasMaxLength(32);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.CreatedByLogin).HasMaxLength(200);
        builder.Property(e => e.UpdatedByLogin).HasMaxLength(200);
        builder.Property(e => e.ClearedByLogin).HasMaxLength(200);
        builder.Property(e => e.ObservedHoursSinceLastBill).HasColumnType("decimal(18,4)");
        builder.HasIndex(e => e.ProjectId, "UX_BillingReviewDecision_ProjectID")
            .IsUnique();
    }
}
