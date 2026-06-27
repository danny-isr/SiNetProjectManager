using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowDefinition",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinition", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStageDefinition",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionID = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStageDefinition", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowStageDefinition_Definition",
                        column: x => x.WorkflowDefinitionID,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstance",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionID = table.Column<int>(type: "int", nullable: false),
                    ProjectID = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrentStageID = table.Column<int>(type: "int", nullable: true),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    TriggerEntityID = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstance", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowInstance_CreatedByUser",
                        column: x => x.CreatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_WorkflowInstance_CurrentStage",
                        column: x => x.CurrentStageID,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_WorkflowInstance_Definition",
                        column: x => x.WorkflowDefinitionID,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowInstance_Project",
                        column: x => x.ProjectID,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionRule",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionID = table.Column<int>(type: "int", nullable: false),
                    FromStageID = table.Column<int>(type: "int", nullable: false),
                    ToStageID = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitionRule", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionRule_Definition",
                        column: x => x.WorkflowDefinitionID,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionRule_FromStage",
                        column: x => x.FromStageID,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionRule_ToStage",
                        column: x => x.ToStageID,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStageTransition",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceID = table.Column<int>(type: "int", nullable: false),
                    ToStageID = table.Column<int>(type: "int", nullable: false),
                    FromStageID = table.Column<int>(type: "int", nullable: true),
                    TransitionedByUserID = table.Column<int>(type: "int", nullable: false),
                    TransitionedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStageTransition", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowStageTransition_Instance",
                        column: x => x.WorkflowInstanceID,
                        principalTable: "WorkflowInstance",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowStageTransition_ToStage",
                        column: x => x.ToStageID,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_WorkflowStageTransition_User",
                        column: x => x.TransitionedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_Code",
                table: "WorkflowDefinition",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_CreatedByUserID",
                table: "WorkflowInstance",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_CurrentStageID",
                table: "WorkflowInstance",
                column: "CurrentStageID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_Project",
                table: "WorkflowInstance",
                column: "ProjectID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_ProjectStatus",
                table: "WorkflowInstance",
                columns: new[] { "ProjectID", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_Status",
                table: "WorkflowInstance",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstance_WorkflowDefinitionID",
                table: "WorkflowInstance",
                column: "WorkflowDefinitionID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageDefinition_DefCode",
                table: "WorkflowStageDefinition",
                columns: new[] { "WorkflowDefinitionID", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageDefinition_DefSort",
                table: "WorkflowStageDefinition",
                columns: new[] { "WorkflowDefinitionID", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTransition_InstanceTime",
                table: "WorkflowStageTransition",
                columns: new[] { "WorkflowInstanceID", "TransitionedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTransition_ToStageID",
                table: "WorkflowStageTransition",
                column: "ToStageID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageTransition_TransitionedByUserID",
                table: "WorkflowStageTransition",
                column: "TransitionedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionRule_FromStageID",
                table: "WorkflowTransitionRule",
                column: "FromStageID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionRule_ToStageID",
                table: "WorkflowTransitionRule",
                column: "ToStageID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionRule_Unique",
                table: "WorkflowTransitionRule",
                columns: new[] { "WorkflowDefinitionID", "FromStageID", "ToStageID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStageTransition");

            migrationBuilder.DropTable(
                name: "WorkflowTransitionRule");

            migrationBuilder.DropTable(
                name: "WorkflowInstance");

            migrationBuilder.DropTable(
                name: "WorkflowStageDefinition");

            migrationBuilder.DropTable(
                name: "WorkflowDefinition");
        }
    }
}
