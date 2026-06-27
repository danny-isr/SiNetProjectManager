using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStageTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowStageTask",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageDefinitionID = table.Column<int>(type: "int", nullable: false),
                    TaskTypeID = table.Column<int>(type: "int", nullable: false),
                    DefaultAssigneeID = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStageTask", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowStageTask_DefaultAssignee",
                        column: x => x.DefaultAssigneeID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowStageTask_StageDefinition",
                        column: x => x.StageDefinitionID,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowStageTask_TaskType",
                        column: x => x.TaskTypeID,
                        principalTable: "TaskType",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTask_DefaultAssigneeID",
                table: "WorkflowStageTask",
                column: "DefaultAssigneeID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTask_Stage",
                table: "WorkflowStageTask",
                column: "StageDefinitionID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTask_Stage_TaskType",
                table: "WorkflowStageTask",
                columns: new[] { "StageDefinitionID", "TaskTypeID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTask_TaskTypeID",
                table: "WorkflowStageTask",
                column: "TaskTypeID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStageTask");
        }
    }
}
