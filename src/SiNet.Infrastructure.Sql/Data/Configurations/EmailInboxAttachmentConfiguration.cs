using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// Fluent API configuration for EmailInboxAttachment entity.
/// Defines table structure, indexes, and constraints.
/// </summary>
public class EmailInboxAttachmentConfiguration : IEntityTypeConfiguration<EmailInboxAttachment>
{
    public void Configure(EntityTypeBuilder<EmailInboxAttachment> builder)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Table Name
        // ═══════════════════════════════════════════════════════════════════════
        builder.ToTable("EmailInboxAttachment");

        // ═══════════════════════════════════════════════════════════════════════
        // Primary Key
        // ═══════════════════════════════════════════════════════════════════════
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        // ═══════════════════════════════════════════════════════════════════════
        // Columns
        // ═══════════════════════════════════════════════════════════════════════
        builder.Property(e => e.MessageId)
            .IsRequired();

        builder.Property(e => e.AttachmentIndex)
            .IsRequired();

        builder.Property(e => e.OriginalFileName)
            .HasMaxLength(260)
            .IsUnicode(true);

        builder.Property(e => e.SavedFileName)
            .HasMaxLength(260)
            .IsUnicode(true);

        builder.Property(e => e.ContentSha256)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength(true) // char(64) instead of varchar(64)
            .IsUnicode(false); // ASCII hex characters only

        builder.Property(e => e.AccItemId)
            .HasMaxLength(128);

        builder.Property(e => e.AccVersionId)
            .HasMaxLength(128);

        // ═══════════════════════════════════════════════════════════════════════
        // Indexes
        // ═══════════════════════════════════════════════════════════════════════

        // Index on MessageId for FK lookups
        builder.HasIndex(e => e.MessageId)
            .HasDatabaseName("IX_EmailInboxAttachment_MessageId");

        // UNIQUE constraint on (MessageId, ContentSha256) - prevents duplicate attachments
        builder.HasIndex(e => new { e.MessageId, e.ContentSha256 })
            .IsUnique()
            .HasDatabaseName("UQ_EmailInboxAttachment_MessageId_ContentSha256");

        // ═══════════════════════════════════════════════════════════════════════
        // Relationships
        // ═══════════════════════════════════════════════════════════════════════

        // FK to EmailInboxMessage table (configured in EmailInboxMessageConfiguration)
        builder.HasOne(e => e.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_EmailInboxAttachment_Message");

        // ═══════════════════════════════════════════════════════════════════════
        // Tagging FK: ProjectFile (target folder type for ACC filing)
        // ═══════════════════════════════════════════════════════════════════════
        builder.Property(e => e.ProjectFileId)
            .IsRequired(false);

        builder.HasOne(e => e.ProjectFile)
            .WithMany()
            .HasForeignKey(e => e.ProjectFileId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_EmailInboxAttachment_ProjectFile");

        builder.HasIndex(e => e.ProjectFileId)
            .HasDatabaseName("IX_EmailInboxAttachment_ProjectFileId");
    }
}
