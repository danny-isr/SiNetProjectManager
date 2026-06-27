using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultAlternativeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProjectAlternative",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "1",
                collation: "Hebrew_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldCollation: "Hebrew_100_CI_AS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ProjectAlternative",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                collation: "Hebrew_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldDefaultValue: "1",
                oldCollation: "Hebrew_100_CI_AS");
        }
    }
}
