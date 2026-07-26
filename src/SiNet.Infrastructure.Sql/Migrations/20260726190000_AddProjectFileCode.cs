using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SiNetSQL.Data;

#nullable disable

namespace SiNetSQL.Migrations;

/// <inheritdoc />
[DbContext(typeof(SiNetSQLDbContext))]
[Migration("20260726190000_AddProjectFileCode")]
public partial class AddProjectFileCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.ProjectFile', N'Code') IS NULL
            BEGIN
                ALTER TABLE [dbo].[ProjectFile]
                ADD [Code] nvarchar(64) NULL;
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'ux_ProjectFile_Code'
                  AND [object_id] = OBJECT_ID(N'dbo.ProjectFile'))
            BEGIN
                CREATE UNIQUE NONCLUSTERED INDEX [ux_ProjectFile_Code]
                ON [dbo].[ProjectFile] ([Code])
                WHERE [Code] IS NOT NULL;
            END
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'ux_ProjectFile_Code'
                  AND [object_id] = OBJECT_ID(N'dbo.ProjectFile'))
            BEGIN
                DROP INDEX [ux_ProjectFile_Code] ON [dbo].[ProjectFile];
            END

            IF COL_LENGTH(N'dbo.ProjectFile', N'Code') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[ProjectFile] DROP COLUMN [Code];
            END
            """);
    }
}
