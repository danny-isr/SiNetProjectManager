using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileIsRequiredAndCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ProjectFile",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "Hebrew_100_CI_AS");

            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "ProjectFile",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ux_ProjectFile_Code",
                table: "ProjectFile",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ProjectFile_Code",
                table: "ProjectFile");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ProjectFile");

            migrationBuilder.DropColumn(
                name: "IsRequired",
                table: "ProjectFile");
        }
    }
}
