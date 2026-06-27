using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTypeWorkflowDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectTypeWorkflowDefinition",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectTypeID = table.Column<int>(type: "int", nullable: false),
                    WorkflowDefinitionID = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTypeWorkflowDefinition", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ProjectTypeWorkflowDefinition_JobType",
                        column: x => x.ProjectTypeID,
                        principalTable: "JobType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTypeWorkflowDefinition_WorkflowDefinition",
                        column: x => x.WorkflowDefinitionID,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeWorkflowDefinition_ProjectType",
                table: "ProjectTypeWorkflowDefinition",
                column: "ProjectTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeWorkflowDefinition_Unique",
                table: "ProjectTypeWorkflowDefinition",
                columns: new[] { "ProjectTypeID", "WorkflowDefinitionID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeWorkflowDefinition_WorkflowDefinitionID",
                table: "ProjectTypeWorkflowDefinition",
                column: "WorkflowDefinitionID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectTypeWorkflowDefinition");
        }
    }
}
