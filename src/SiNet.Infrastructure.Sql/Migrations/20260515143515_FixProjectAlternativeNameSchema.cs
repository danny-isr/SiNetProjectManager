using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class FixProjectAlternativeNameSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectAlternative_Project_NormalizedName",
                table: "ProjectAlternative");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "ProjectAlternative",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldDefaultValue: "1");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProjectAlternative",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                collation: "Hebrew_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldDefaultValue: "1",
                oldCollation: "Hebrew_100_CI_AS");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectAlternative_ProjectID_NormalizedName",
                table: "ProjectAlternative",
                columns: new[] { "ProjectID", "NormalizedName" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectAlternative_ProjectID_NormalizedName",
                table: "ProjectAlternative");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "ProjectAlternative",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "1",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProjectAlternative",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "1",
                collation: "Hebrew_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldCollation: "Hebrew_100_CI_AS");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_Project_NormalizedName",
                table: "ProjectAlternative",
                columns: new[] { "ProjectID", "NormalizedName" },
                unique: true);
        }
    }
}
