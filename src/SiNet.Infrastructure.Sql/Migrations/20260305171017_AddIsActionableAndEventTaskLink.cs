using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActionableAndEventTaskLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActionable",
                table: "ProjectAssignmentStatus",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TaskLinkID",
                table: "ProjectAssignmentEvent",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_TaskLinkID",
                table: "ProjectAssignmentEvent",
                column: "TaskLinkID");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignmentEvent_TaskLink",
                table: "ProjectAssignmentEvent",
                column: "TaskLinkID",
                principalTable: "TaskLink",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignmentEvent_TaskLink",
                table: "ProjectAssignmentEvent");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignmentEvent_TaskLinkID",
                table: "ProjectAssignmentEvent");

            migrationBuilder.DropColumn(
                name: "IsActionable",
                table: "ProjectAssignmentStatus");

            migrationBuilder.DropColumn(
                name: "TaskLinkID",
                table: "ProjectAssignmentEvent");
        }
    }
}
