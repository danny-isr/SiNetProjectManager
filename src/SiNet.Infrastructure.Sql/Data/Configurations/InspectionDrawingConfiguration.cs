using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

public class InspectionReportDrawingConfiguration : IEntityTypeConfiguration<InspectionReportDrawing>
{
    public void Configure(EntityTypeBuilder<InspectionReportDrawing> builder)
    {
        builder.ToTable("InspectionReportDrawings");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.SourceFilePath).HasMaxLength(500).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(260).IsRequired();
        builder.Property(d => d.SelectedLayoutIndices).HasMaxLength(500).IsRequired()
            .HasDefaultValue("[]");
        builder.Property(d => d.StampedFilePath).HasMaxLength(500);

        builder.Property(d => d.FileType)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(d => d.StampStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(DrawingStampStatus.NotStamped);

        builder.HasIndex(d => d.ReportId, "IX_InspectionReportDrawings_ReportId");

        builder.HasOne(d => d.Report)
            .WithMany(r => r.Drawings)
            .HasForeignKey(d => d.ReportId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_InspectionReportDrawing_Report");
    }
}

public class InspectionSeriesFileConfigConfiguration : IEntityTypeConfiguration<InspectionSeriesFileConfig>
{
    public void Configure(EntityTypeBuilder<InspectionSeriesFileConfig> builder)
    {
        builder.ToTable("InspectionSeriesFileConfigs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Unique: a series cannot have the same file in the same role twice
        builder.HasIndex(c => new { c.SeriesId, c.ProjectFileId, c.Role },
                "IX_SeriesFileConfigs_Series_File_Role")
            .IsUnique();

        builder.HasOne(c => c.Series)
            .WithMany(s => s.FileConfigs)
            .HasForeignKey(c => c.SeriesId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_SeriesFileConfig_Series");

        builder.HasOne(c => c.ProjectFile)
            .WithMany()
            .HasForeignKey(c => c.ProjectFileId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_SeriesFileConfig_ProjectFile");
    }
}
