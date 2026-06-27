using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core configuration for the Inspection System tables:
/// InspectionReport, Chapter, Section, CommentsBank, InspectionNote.
/// </summary>
public class InspectionReportConfiguration : IEntityTypeConfiguration<InspectionReport>
{
    public void Configure(EntityTypeBuilder<InspectionReport> builder)
    {
        builder.ToTable("InspectionReports");

        builder.HasKey(e => e.ReportId);
        builder.Property(e => e.ReportId).HasColumnName("ReportId");

        builder.Property(e => e.ProjectId).HasColumnName("ProjectId");
        builder.Property(e => e.ReportNumber);
        builder.Property(e => e.InspectionDate).HasColumnType("datetime2");

        builder.Property(e => e.InspectorName)
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(e => e.SourceFileUrn)
            .IsUnicode(true);

        builder.Property(e => e.SourceFileVersion)
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.HasIndex(e => e.ProjectId, "IX_InspectionReports_ProjectId");

        // ── Per-series uniqueness ──
        // ReportNumber is unique within an InspectionSeries: each series has its own
        // independent sequence and starts at 1. Two reports under the same project
        // but different series may both have ReportNumber=1.
        // Filtered: only enforced when SeriesId IS NOT NULL.
        builder.HasIndex(e => new { e.ProjectId, e.SeriesId, e.ReportNumber },
                "IX_InspectionReports_Project_Series_Number")
            .IsUnique()
            .HasFilter("[SeriesId] IS NOT NULL");

        // ── Legacy (no-series) uniqueness ──
        // Reports created before the series concept (SeriesId IS NULL) keep the
        // legacy project-scoped uniqueness so we don't lose protection for them.
        builder.HasIndex(e => new { e.ProjectId, e.ReportNumber },
                "IX_InspectionReports_Project_Number_NoSeries")
            .IsUnique()
            .HasFilter("[SeriesId] IS NULL");

        // FK to Project (restrict — cannot delete project while reports reference it)
        builder.HasOne(d => d.Project)
            .WithMany(p => p.InspectionReports)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InspectionReport_Project");

        // FK to InspectionSeries (nullable, restrict — optional grouping by series)
        builder.HasOne(d => d.Series)
            .WithMany(s => s.InspectionReports)
            .HasForeignKey(d => d.SeriesId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InspectionReport_Series");

        // FK to Siuser as Inspector (nullable, no action — optional inspector binding)
        builder.HasOne(d => d.Inspector)
            .WithMany(p => p.InspectionReportsAsInspector)
            .HasForeignKey(d => d.InspectorId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InspectionReport_Inspector");
    }
}

public class ChapterNameConfiguration : IEntityTypeConfiguration<ChapterName>
{
    public void Configure(EntityTypeBuilder<ChapterName> builder)
    {
        builder.ToTable("ChapterNames");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .HasMaxLength(500)
            .IsUnicode(true)
            .IsRequired();

        builder.HasIndex(e => e.Name, "IX_ChapterNames_Name")
            .IsUnique();
    }
}

public class SectionNameConfiguration : IEntityTypeConfiguration<SectionName>
{
    public void Configure(EntityTypeBuilder<SectionName> builder)
    {
        builder.ToTable("SectionNames");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .HasMaxLength(500)
            .IsUnicode(true)
            .IsRequired();

        builder.HasIndex(e => e.Name, "IX_SectionNames_Name")
            .IsUnique();
    }
}

public class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("Chapters");

        builder.HasKey(e => e.ChapterId);
        builder.Property(e => e.ChapterId).HasColumnName("ChapterId");

        builder.Property(e => e.SeriesId).HasColumnName("SeriesId");
        builder.Property(e => e.ChapterNumber);
        builder.Property(e => e.ChapterNameId).HasColumnName("ChapterNameId");

        builder.HasIndex(e => new { e.SeriesId, e.ChapterNumber },
                "IX_Chapters_Series_Number")
            .IsUnique();

        // FK to InspectionSeries (nullable, restrict — optional template series grouping)
        builder.HasOne(d => d.Series)
            .WithMany(s => s.Chapters)
            .HasForeignKey(d => d.SeriesId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Chapter_Series");

        // FK to ChapterName dictionary (restrict — cannot delete name while chapters reference it)
        builder.HasOne(d => d.ChapterName)
            .WithMany(c => c.Chapters)
            .HasForeignKey(d => d.ChapterNameId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Chapter_ChapterName");
    }
}

public class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.ToTable("Sections");

        builder.HasKey(e => e.SectionId);
        builder.Property(e => e.SectionId).HasColumnName("SectionId");

        builder.Property(e => e.ChapterId).HasColumnName("ChapterId");
        builder.Property(e => e.SectionNameId).HasColumnName("SectionNameId");

        builder.Property(e => e.SectionCode);

        builder.Property(e => e.Version)
            .HasDefaultValue(1);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(e => e.ChapterId, "IX_Sections_ChapterId");

        // Unique composite index — ensures no duplicate (ChapterId, SectionCode, Version)
        builder.HasIndex(e => new { e.ChapterId, e.SectionCode, e.Version },
                "IX_Sections_Chapter_Code_Version")
            .IsUnique();

        // Filtered index — fast lookup of active sections per chapter
        builder.HasIndex(e => new { e.ChapterId, e.SectionCode },
                "IX_Sections_ActiveByChapter")
            .HasFilter("[IsActive] = 1");

        // Unique: within a chapter, only one active section can have a given SectionName
        builder.HasIndex(e => new { e.ChapterId, e.SectionNameId },
                "IX_Sections_ActiveChapterName")
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        // FK to Chapter (restrict — cannot delete chapter while sections exist)
        builder.HasOne(d => d.Chapter)
            .WithMany(c => c.Sections)
            .HasForeignKey(d => d.ChapterId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Section_Chapter");

        // FK to SectionName dictionary (restrict — cannot delete name while sections reference it)
        builder.HasOne(d => d.SectionName)
            .WithMany(n => n.Sections)
            .HasForeignKey(d => d.SectionNameId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Section_SectionName");

        // FullCode is [NotMapped] — EF will ignore it automatically
    }
}

public class CommentsBankConfiguration : IEntityTypeConfiguration<CommentsBank>
{
    public void Configure(EntityTypeBuilder<CommentsBank> builder)
    {
        builder.ToTable("CommentsBank");

        builder.HasKey(e => e.CommentId);
        builder.Property(e => e.CommentId).HasColumnName("CommentId");

        builder.Property(e => e.SectionId).HasColumnName("SectionId");

        builder.Property(e => e.CommonText)
            .IsUnicode(true);

        builder.HasIndex(e => e.SectionId, "IX_CommentsBank_SectionId");

        // FK to Section (restrict — cannot delete section while comments exist)
        builder.HasOne(d => d.Section)
            .WithMany(s => s.CommentsBank)
            .HasForeignKey(d => d.SectionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_CommentsBank_Section");
    }
}

public class InspectionNoteConfiguration : IEntityTypeConfiguration<InspectionNote>
{
    public void Configure(EntityTypeBuilder<InspectionNote> builder)
    {
        builder.ToTable("InspectionNotes");

        builder.HasKey(e => e.NoteId);
        builder.Property(e => e.NoteId).HasColumnName("NoteId");

        builder.Property(e => e.ReportId).HasColumnName("ReportId");
        builder.Property(e => e.SectionId).HasColumnName("SectionId");
        builder.Property(e => e.PreviousNoteId).HasColumnName("PreviousNoteId");

        builder.Property(e => e.NoteSubIndex)
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(e => e.NoteText)
            .IsUnicode(true);

        builder.Property(e => e.NoteStatus)
            .HasMaxLength(50)
            .IsUnicode(true);

        builder.Property(e => e.AccMarkupLink)
            .HasMaxLength(1000)
            .IsUnicode(true);

        // ── Logical link to a specific reviewed file (Work Window 2) ──
        builder.Property(e => e.LinkedFileName)
            .HasMaxLength(500)
            .IsUnicode(true);
        builder.Property(e => e.LinkedAlternative)
            .HasMaxLength(255)
            .IsUnicode(true);
        builder.Property(e => e.LinkedVersion)
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.HasIndex(e => e.ReportId, "IX_InspectionNotes_ReportId");
        builder.HasIndex(e => e.SectionId, "IX_InspectionNotes_SectionId");
        builder.HasIndex(e => e.PreviousNoteId, "IX_InspectionNotes_PreviousNoteId");

        // Unique: a report cannot have two notes with the same section + sub-index
        builder.HasIndex(e => new { e.ReportId, e.SectionId, e.NoteSubIndex },
                "IX_InspectionNotes_Report_Section_SubIndex")
            .IsUnique();

        // FK to InspectionReport (cascade — delete notes when report is deleted)
        builder.HasOne(d => d.InspectionReport)
            .WithMany(r => r.InspectionNotes)
            .HasForeignKey(d => d.ReportId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_InspectionNote_Report");

        // FK to Section (no action — avoid multiple cascade paths)
        builder.HasOne(d => d.Section)
            .WithMany(s => s.InspectionNotes)
            .HasForeignKey(d => d.SectionId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InspectionNote_Section");

        // Self-referencing FK (no action — avoid multiple cascade paths)
        builder.HasOne(d => d.PreviousNote)
            .WithMany(p => p.FollowUpNotes)
            .HasForeignKey(d => d.PreviousNoteId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InspectionNote_PreviousNote");

        // FK to InspectionNoteStatus (nullable, no action — optional normalized status)
        builder.HasOne(d => d.NoteStatusLookup)
            .WithMany(s => s.InspectionNotes)
            .HasForeignKey(d => d.NoteStatusId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InspectionNote_NoteStatus");
    }
}

public class InspectionNoteStatusConfiguration : IEntityTypeConfiguration<InspectionNoteStatus>
{
    public void Configure(EntityTypeBuilder<InspectionNoteStatus> builder)
    {
        builder.ToTable("InspectionNoteStatuses");

        builder.HasKey(e => e.StatusId);
        builder.Property(e => e.StatusId).HasColumnName("StatusId");

        builder.Property(e => e.StatusKey)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.HebrewLabel)
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(e => e.ExportSymbol)
            .HasMaxLength(10);

        builder.Property(e => e.SortOrder);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(e => e.StatusKey, "IX_InspectionNoteStatuses_StatusKey")
            .IsUnique();

        // Seed the standard inspection statuses
        builder.HasData(
            new InspectionNoteStatus { StatusId = 1, StatusKey = "Passed", HebrewLabel = "\u05de\u05e7\u05d5\u05d1\u05dc", SortOrder = 1, ExportSymbol = "V", IsActive = true },
            new InspectionNoteStatus { StatusId = 2, StatusKey = "Failed", HebrewLabel = "\u05d4\u05e2\u05e8\u05d4", SortOrder = 2, ExportSymbol = "X", IsActive = true },
            new InspectionNoteStatus { StatusId = 3, StatusKey = "RecurringFailed", HebrewLabel = "\u05d4\u05e2\u05e8\u05d4 \u05d7\u05d5\u05d6\u05e8\u05ea", SortOrder = 3, ExportSymbol = "!", IsActive = true },
            new InspectionNoteStatus { StatusId = 4, StatusKey = "NotApplicable", HebrewLabel = "\u05dc\u05d0 \u05e8\u05dc\u05d5\u05d5\u05e0\u05d8\u05d9", SortOrder = 4, ExportSymbol = "\u2014", IsActive = true },
            new InspectionNoteStatus { StatusId = 5, StatusKey = "ManagerReview", HebrewLabel = "\u05d4\u05e2\u05e8\u05d4 \u05dc\u05d1\u05d3\u05d9\u05e7\u05ea \u05d4\u05de\u05e0\u05d4\u05dc", SortOrder = 5, ExportSymbol = "?", IsActive = true }
        );
    }
}

public class InspectionSeriesConfiguration : IEntityTypeConfiguration<InspectionSeries>
{
    public void Configure(EntityTypeBuilder<InspectionSeries> builder)
    {
        builder.ToTable("InspectionSeries");

        builder.HasKey(e => e.SeriesId);
        builder.Property(e => e.SeriesId).HasColumnName("SeriesId");

        builder.Property(e => e.ProjectId).HasColumnName("ProjectId");

        builder.Property(e => e.SeriesName)
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(e => e.TemplateUrl)
            .IsUnicode(true);

        builder.Property(e => e.TemplateSpreadsheetId)
            .HasMaxLength(255);

        builder.Property(e => e.Created)
            .HasColumnType("datetime2");

        builder.Property(e => e.Modified)
            .HasColumnType("datetime2");

        builder.HasIndex(e => e.ProjectId, "IX_InspectionSeries_ProjectId");

        // Unique: a project cannot have two series with the same name (when name is set)
        builder.HasIndex(e => new { e.ProjectId, e.SeriesName },
                "IX_InspectionSeries_Project_Name")
            .IsUnique()
            .HasFilter("[SeriesName] IS NOT NULL");

        // Non-unique: multiple series CAN share the same template for a project
        builder.HasIndex(e => new { e.ProjectId, e.TemplateSpreadsheetId },
                "IX_InspectionSeries_Project_Template")
            .HasFilter("[TemplateSpreadsheetId] IS NOT NULL");

        // FK to Project (restrict — cannot delete project while series reference it)
        builder.HasOne(d => d.Project)
            .WithMany(p => p.InspectionSeries)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InspectionSeries_Project");
    }
}

public class InspectionReportReviewedFileConfiguration : IEntityTypeConfiguration<InspectionReportReviewedFile>
{
    public void Configure(EntityTypeBuilder<InspectionReportReviewedFile> builder)
    {
        builder.ToTable("InspectionReportReviewedFiles");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ReportId).HasColumnName("ReportId");

        builder.Property(e => e.FileName)
            .HasMaxLength(500)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(e => e.Alternative)
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(e => e.SortOrder);

        builder.HasIndex(e => e.ReportId, "IX_InspectionReportReviewedFiles_ReportId");

        // Same logical entry should not be added twice to the same report
        builder.HasIndex(e => new { e.ReportId, e.FileName, e.Alternative },
                "IX_InspectionReportReviewedFiles_Report_File_Alt")
            .IsUnique();

        builder.HasOne(d => d.InspectionReport)
            .WithMany(r => r.ReviewedFiles)
            .HasForeignKey(d => d.ReportId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_InspectionReportReviewedFile_Report");
    }
}
