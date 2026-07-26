using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SiNetSQL.Data;

#nullable disable

namespace SiNetSQL.Migrations;

/// <inheritdoc />
[DbContext(typeof(SiNetSQLDbContext))]
[Migration("20260726140000_AddProjectFileIsRequired")]
public partial class AddProjectFileIsRequired : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent: production disables auto-Migrate and may already have been patched at runtime.
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.ProjectFile', N'IsRequired') IS NULL
            BEGIN
                ALTER TABLE [dbo].[ProjectFile]
                ADD [IsRequired] bit NOT NULL
                    CONSTRAINT [DF_ProjectFile_IsRequired] DEFAULT (CONVERT([bit],(0)));
            END
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.ProjectFile', N'IsRequired') IS NOT NULL
            BEGIN
                DECLARE @df sysname;
                SELECT @df = [name]
                FROM sys.default_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.ProjectFile')
                  AND COL_NAME([parent_object_id], [parent_column_id]) = N'IsRequired';
                IF @df IS NOT NULL
                    EXEC(N'ALTER TABLE [dbo].[ProjectFile] DROP CONSTRAINT [' + @df + N']');
                ALTER TABLE [dbo].[ProjectFile] DROP COLUMN [IsRequired];
            END
            """);
    }
}
