using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiNetSQL.Models;

namespace SiNetSQL.Data.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="SystemSetting"/> table.
/// Key-value pair store for global application settings.
/// </summary>
public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");

        builder.HasKey(e => e.SettingKey);

        builder.Property(e => e.SettingKey)
            .HasMaxLength(128)
            .IsUnicode(false);

        builder.Property(e => e.SettingValue)
            .IsRequired()
            .IsUnicode(true);

        builder.Property(e => e.Description)
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(e => e.LastUpdated)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
