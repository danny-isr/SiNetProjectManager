using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterPlanMappingIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MasterPlanContactId",
                table: "Contacts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MasterPlanCompanyId",
                table: "Company",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_MasterPlanContactId",
                table: "Contacts",
                column: "MasterPlanContactId",
                unique: true,
                filter: "[MasterPlanContactId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Company_MasterPlanCompanyId",
                table: "Company",
                column: "MasterPlanCompanyId",
                unique: true,
                filter: "[MasterPlanCompanyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_MasterPlanContactId",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Company_MasterPlanCompanyId",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "MasterPlanContactId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "MasterPlanCompanyId",
                table: "Company");
        }
    }
}
