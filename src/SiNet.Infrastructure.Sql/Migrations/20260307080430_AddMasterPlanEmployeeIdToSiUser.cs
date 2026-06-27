using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterPlanEmployeeIdToSiUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MasterPlanEmployeeId",
                table: "SIUser",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SIUser_MasterPlanEmployeeId",
                table: "SIUser",
                column: "MasterPlanEmployeeId",
                unique: true,
                filter: "[MasterPlanEmployeeId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SIUser_MasterPlanEmployeeId",
                table: "SIUser");

            migrationBuilder.DropColumn(
                name: "MasterPlanEmployeeId",
                table: "SIUser");
        }
    }
}
