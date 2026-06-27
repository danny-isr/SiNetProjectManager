using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskBehaviorSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskBehaviorDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaskTypeId = table.Column<int>(type: "int", nullable: true),
                    AutoCreateOnTrigger = table.Column<bool>(type: "bit", nullable: false),
                    AutoCloseOnCompletion = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskBehaviorDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskBehaviorDefinitions_TaskType_TaskTypeId",
                        column: x => x.TaskTypeId,
                        principalTable: "TaskType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskCompletionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BehaviorDefinitionId = table.Column<int>(type: "int", nullable: false),
                    CompletionType = table.Column<int>(type: "int", nullable: false),
                    ConditionJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResultingStatusId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCompletionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCompletionRules_ProjectAssignmentStatus_ResultingStatusId",
                        column: x => x.ResultingStatusId,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskCompletionRules_TaskBehaviorDefinitions_BehaviorDefinitionId",
                        column: x => x.BehaviorDefinitionId,
                        principalTable: "TaskBehaviorDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskTriggerRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BehaviorDefinitionId = table.Column<int>(type: "int", nullable: false),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    ConditionJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTriggerRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTriggerRules_TaskBehaviorDefinitions_BehaviorDefinitionId",
                        column: x => x.BehaviorDefinitionId,
                        principalTable: "TaskBehaviorDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskBehaviorDefinition_Code",
                table: "TaskBehaviorDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskBehaviorDefinitions_TaskTypeId",
                table: "TaskBehaviorDefinitions",
                column: "TaskTypeId",
                unique: true,
                filter: "[TaskTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletionRules_BehaviorDefinitionId",
                table: "TaskCompletionRules",
                column: "BehaviorDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletionRules_ResultingStatusId",
                table: "TaskCompletionRules",
                column: "ResultingStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTriggerRules_BehaviorDefinitionId",
                table: "TaskTriggerRules",
                column: "BehaviorDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskCompletionRules");

            migrationBuilder.DropTable(
                name: "TaskTriggerRules");

            migrationBuilder.DropTable(
                name: "TaskBehaviorDefinitions");
        }
    }
}
