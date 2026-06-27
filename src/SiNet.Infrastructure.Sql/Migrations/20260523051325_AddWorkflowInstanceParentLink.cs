using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceParentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentWorkflowInstanceID",
                table: "WorkflowInstance",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_Parent",
                table: "WorkflowInstance",
                column: "ParentWorkflowInstanceID");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowInstance_Parent",
                table: "WorkflowInstance",
                column: "ParentWorkflowInstanceID",
                principalTable: "WorkflowInstance",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowInstance_Parent",
                table: "WorkflowInstance");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstance_Parent",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "ParentWorkflowInstanceID",
                table: "WorkflowInstance");
        }
    }
}
