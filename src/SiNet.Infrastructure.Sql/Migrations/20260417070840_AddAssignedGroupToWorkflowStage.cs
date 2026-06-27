using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedGroupToWorkflowStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedGroupID",
                table: "WorkflowStageDefinition",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageDefinition_AssignedGroupID",
                table: "WorkflowStageDefinition",
                column: "AssignedGroupID");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowStageDefinition_AssignedGroup",
                table: "WorkflowStageDefinition",
                column: "AssignedGroupID",
                principalTable: "UserGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowStageDefinition_AssignedGroup",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowStageDefinition_AssignedGroupID",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "AssignedGroupID",
                table: "WorkflowStageDefinition");
        }
    }
}
