using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public sealed class MasterPlanBackupIntakeConfiguration : IEntityTypeConfiguration<MasterPlanBackupIntake>
{
    public void Configure(EntityTypeBuilder<MasterPlanBackupIntake> builder)
    {
        builder.ToTable("MasterPlanBackupIntake");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ID");
        builder.Property(e => e.GmailMessageId).HasMaxLength(200);
        builder.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(400);
        builder.Property(e => e.IncomingPath).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Sha256).HasMaxLength(64);
        builder.Property(e => e.ReceivedBy).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ResultMessage).HasMaxLength(2000);
        builder.HasIndex(e => e.Sha256, "IX_MasterPlanBackupIntake_Sha256");
        builder.HasIndex(e => e.IncomingPath, "UX_MasterPlanBackupIntake_IncomingPath")
            .IsUnique();
        builder.HasIndex(e => e.Status, "IX_MasterPlanBackupIntake_Status");
    }
}
