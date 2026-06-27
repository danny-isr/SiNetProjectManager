using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultAssigneeToUserGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultAssigneeID",
                table: "UserGroups",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_DefaultAssigneeID",
                table: "UserGroups",
                column: "DefaultAssigneeID");

            migrationBuilder.AddForeignKey(
                name: "FK_UserGroups_DefaultAssignee",
                table: "UserGroups",
                column: "DefaultAssigneeID",
                principalTable: "SIUser",
                principalColumn: "ID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserGroups_DefaultAssignee",
                table: "UserGroups");

            migrationBuilder.DropIndex(
                name: "IX_UserGroups_DefaultAssigneeID",
                table: "UserGroups");

            migrationBuilder.DropColumn(
                name: "DefaultAssigneeID",
                table: "UserGroups");
        }
    }
}
