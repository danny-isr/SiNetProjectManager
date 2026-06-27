using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterPlanSyncField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MasterPlanSync",
                table: "Contacts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MasterPlanSync",
                table: "Company",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Company",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                collation: "Hebrew_100_CI_AS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MasterPlanSync",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "MasterPlanSync",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Company");
        }
    }
}
