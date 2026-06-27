using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInternetMessageIdDefaultConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @constraintName sysname;

SELECT @constraintName = dc.name
FROM sys.default_constraints AS dc
INNER JOIN sys.columns AS c
    ON dc.parent_object_id = c.object_id
   AND dc.parent_column_id = c.column_id
INNER JOIN sys.tables AS t
    ON t.object_id = dc.parent_object_id
INNER JOIN sys.schemas AS s
    ON s.schema_id = t.schema_id
WHERE s.name = 'dbo'
  AND t.name = 'EmailInboxMessage'
  AND c.name = 'InternetMessageId';

IF @constraintName IS NOT NULL
BEGIN
    DECLARE @sql nvarchar(max) =
        N'ALTER TABLE [dbo].[EmailInboxMessage] DROP CONSTRAINT ' + QUOTENAME(@constraintName) + N';';
    EXEC sp_executesql @sql;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS dc
    INNER JOIN sys.columns AS c
        ON dc.parent_object_id = c.object_id
       AND dc.parent_column_id = c.column_id
    INNER JOIN sys.tables AS t
        ON t.object_id = dc.parent_object_id
    INNER JOIN sys.schemas AS s
        ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'EmailInboxMessage'
      AND c.name = 'InternetMessageId'
)
BEGIN
    ALTER TABLE [dbo].[EmailInboxMessage]
        ADD DEFAULT N'' FOR [InternetMessageId];
END
");
        }
    }
}
