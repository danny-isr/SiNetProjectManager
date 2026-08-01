using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowInstance_JobTypeTrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobTypeID",
                table: "WorkflowInstance",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_JobTypeID",
                table: "WorkflowInstance",
                column: "JobTypeID");

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowInstance_ActiveTrack",
                table: "WorkflowInstance",
                columns: new[] { "ProjectID", "WorkflowDefinitionID", "JobTypeID" },
                unique: true,
                filter: "[JobTypeID] IS NOT NULL AND [Status] IN (1, 2)");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowInstance_JobType",
                table: "WorkflowInstance",
                column: "JobTypeID",
                principalTable: "JobType",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowInstance_JobType",
                table: "WorkflowInstance");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstance_JobTypeID",
                table: "WorkflowInstance");

            migrationBuilder.DropIndex(
                name: "UX_WorkflowInstance_ActiveTrack",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "JobTypeID",
                table: "WorkflowInstance");
        }
    }
}
