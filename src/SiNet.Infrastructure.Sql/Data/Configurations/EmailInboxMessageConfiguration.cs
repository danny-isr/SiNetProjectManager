using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// Fluent API configuration for EmailInboxMessage entity.
/// Defines table structure, indexes, and constraints.
/// </summary>
public class EmailInboxMessageConfiguration : IEntityTypeConfiguration<EmailInboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailInboxMessage> builder)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Table Name
        // ═══════════════════════════════════════════════════════════════════════
        builder.ToTable("EmailInboxMessage");

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
        builder.Property(e => e.MessageUniqueId)
            .IsRequired()
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(e => e.ProjectId)
            .IsRequired();

        builder.Property(e => e.GmailThreadId)
            .HasMaxLength(64)
            .IsUnicode(false); // Gmail thread IDs are ASCII

        // RFC 2822 identity headers — global, cross-mailbox business identifiers.
        // InternetMessageId is REQUIRED, NOT NULL and UNIQUE (see unique index below).
        builder.Property(e => e.InternetMessageId)
            .IsRequired()
            .HasMaxLength(998)
            .IsUnicode(false); // Message-ID headers are ASCII per RFC 5322

        builder.Property(e => e.InReplyTo)
            .HasMaxLength(998)
            .IsUnicode(false);

        builder.Property(e => e.References)
            .IsUnicode(false); // nvarchar(max) → varchar(max) ASCII

        // Global thread identity (Stage A) — derived from RFC 2822 headers, never
        // from GmailThreadId. Required, NOT NULL, not unique.
        builder.Property(e => e.ThreadUniqueId)
            .IsRequired()
            .HasMaxLength(255)
            .IsUnicode(false);

        builder.Property(e => e.ThreadKey)
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false);

        builder.Property(e => e.FromAddress)
            .HasMaxLength(320)
            .IsUnicode(true);

        builder.Property(e => e.Subject)
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(e => e.ReceivedUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<int>(); // Store enum as int

        builder.Property(e => e.InboxAccProjectId)
            .HasMaxLength(64);

        builder.Property(e => e.InboxAccFolderId)
            .HasMaxLength(128);

        builder.Property(e => e.CreatedByLogin)
            .HasMaxLength(256)
            .IsUnicode(true);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(e => e.UpdatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(e => e.Error)
            .IsUnicode(true); // nvarchar(max)

        // ═══════════════════════════════════════════════════════════════════════
        // Lease-Based Processing Columns
        // ═══════════════════════════════════════════════════════════════════════

        builder.Property(e => e.ProcessingByLogin)
            .HasMaxLength(256)
            .IsUnicode(true); // nvarchar(256)

        builder.Property(e => e.ProcessingStartedAtUtc)
            .HasColumnType("datetime2"); // nullable datetime2

        // ═══════════════════════════════════════════════════════════════════════
        // Indexes
        // ═══════════════════════════════════════════════════════════════════════

        // UNIQUE constraint on MessageUniqueId - enforces deduplication
        builder.HasIndex(e => e.MessageUniqueId)
            .IsUnique()
            .HasDatabaseName("UQ_EmailInboxMessage_MessageUniqueId");

        // UNIQUE constraint on InternetMessageId (RFC 2822 Message-ID).
        // Non-filtered: NOT NULL is enforced at the column level, so every row
        // carries a globally unique RFC 2822 identifier.
        builder.HasIndex(e => e.InternetMessageId)
            .IsUnique()
            .HasDatabaseName("UQ_EmailInboxMessage_InternetMessageId");

        // Index on ProjectId for FK lookups and filtering
        builder.HasIndex(e => e.ProjectId)
            .HasDatabaseName("IX_EmailInboxMessage_ProjectId");

        // Composite index on (Status, ReceivedUtc) for status-based queries
        builder.HasIndex(e => new { e.Status, e.ReceivedUtc })
            .HasDatabaseName("IX_EmailInboxMessage_Status_ReceivedUtc");

        // Index on GmailThreadId for thread-aware queries
        // (e.g., "are there other emails in this thread still assigned to project X?")
        builder.HasIndex(e => e.GmailThreadId)
            .HasDatabaseName("IX_EmailInboxMessage_GmailThreadId")
            .HasFilter("[GmailThreadId] IS NOT NULL");

        // Non-unique indexes for global thread identity lookups (Stage A).
        builder.HasIndex(e => e.ThreadUniqueId)
            .HasDatabaseName("IX_EmailInboxMessage_ThreadUniqueId");

        builder.HasIndex(e => e.ThreadKey)
            .HasDatabaseName("IX_EmailInboxMessage_ThreadKey");

        // ═══════════════════════════════════════════════════════════════════════
        // Relationships
        // ═══════════════════════════════════════════════════════════════════════

        // FK to Projects table
        builder.HasOne(e => e.Project)
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Restrict) // Prevent accidental project deletion
            .HasConstraintName("FK_EmailInboxMessage_Project");

        // One-to-many relationship with EmailInboxAttachment
        builder.HasMany(e => e.Attachments)
            .WithOne(a => a.Message)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade); // Delete attachments when message deleted
    }
}
