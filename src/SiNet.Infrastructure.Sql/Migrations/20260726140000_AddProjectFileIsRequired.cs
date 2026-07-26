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
        migrationBuilder.AddColumn<bool>(
            name: "IsRequired",
            table: "ProjectFile",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsRequired",
            table: "ProjectFile");
    }
}
