using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningWorkflowTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ProjectStatus",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProjectStatus",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ProjectStatus",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ProjectAssignmentStatus",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProjectAssignmentStatus",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // ---------------------------------------------------------------
            // Backfill ProjectStatus.Code from legacy Hebrew titles.
            // Anything not mapped temporarily gets '_Unmapped_' + ID so the
            // NOT NULL + UNIQUE constraint can be applied; the seed service
            // (TaskManagementSeedService.ReconcileProjectStatusesToCanonical)
            // then performs full reconciliation: updates Title/SortOrder,
            // deactivates unmapped rows, and inserts missing canonical rows.
            // Existing Project.ProjectStatusId values remain valid.
            // ---------------------------------------------------------------
            migrationBuilder.Sql(@"
UPDATE [ProjectStatus] SET [Code] = CASE LTRIM(RTRIM([Title]))
    WHEN N'איסוף חומר להצעת מחיר' THEN N'QuotePreparation'
    WHEN N'הצעת מחיר'             THEN N'WaitingForQuoteApproval'
    WHEN N'בטיפול'                THEN N'Active'
    WHEN N'בהמתנה'                THEN N'WaitingForClient'
    WHEN N'הסתיים'                THEN N'Closed'
    WHEN N'הצעה לא מאושרת'         THEN N'ClosedLost'
    WHEN N'לא פרויקט תכנוני'        THEN N'Cancelled'
    ELSE N'_Unmapped_' + CAST([ID] AS NVARCHAR(20))
END
WHERE [Code] = N'';

-- ProjectAssignmentStatus is not in active use; assign legacy codes.
UPDATE [ProjectAssignmentStatus]
SET    [Code] = N'Legacy_' + CAST([ID] AS NVARCHAR(20))
WHERE  [Code] = N'';
");

            migrationBuilder.AddColumn<int>(
                name: "TaskResultId",
                table: "ProjectAssignmentEvent",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastTaskResultId",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectTypeDiscipline",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectTypeId = table.Column<int>(type: "int", nullable: false),
                    DisciplineTaskTypeId = table.Column<int>(type: "int", nullable: false),
                    DefaultAssignedGroupId = table.Column<int>(type: "int", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTypeDiscipline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTypeDiscipline_JobType_ProjectTypeId",
                        column: x => x.ProjectTypeId,
                        principalTable: "JobType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTypeDiscipline_TaskType_DisciplineTaskTypeId",
                        column: x => x.DisciplineTaskTypeId,
                        principalTable: "TaskType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectTypeDiscipline_UserGroups_DefaultAssignedGroupId",
                        column: x => x.DefaultAssignedGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTypeWorkflowStage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectTypeId = table.Column<int>(type: "int", nullable: false),
                    WorkflowStageDefinitionId = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CanRepeat = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTypeWorkflowStage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTypeWorkflowStage_JobType_ProjectTypeId",
                        column: x => x.ProjectTypeId,
                        principalTable: "JobType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTypeWorkflowStage_WorkflowStageDefinition_WorkflowStageDefinitionId",
                        column: x => x.WorkflowStageDefinitionId,
                        principalTable: "WorkflowStageDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskResultDefinition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskResultDefinition", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStatus_Code",
                table: "ProjectStatus",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentStatus_Code",
                table: "ProjectAssignmentStatus",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_TaskResultId",
                table: "ProjectAssignmentEvent",
                column: "TaskResultId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_LastTaskResultId",
                table: "ProjectAssignment",
                column: "LastTaskResultId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeDiscipline_DefaultAssignedGroupId",
                table: "ProjectTypeDiscipline",
                column: "DefaultAssignedGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeDiscipline_DisciplineTaskTypeId",
                table: "ProjectTypeDiscipline",
                column: "DisciplineTaskTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeDiscipline_ProjectType_TaskType",
                table: "ProjectTypeDiscipline",
                columns: new[] { "ProjectTypeId", "DisciplineTaskTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeWorkflowStage_ProjectType_Stage",
                table: "ProjectTypeWorkflowStage",
                columns: new[] { "ProjectTypeId", "WorkflowStageDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeWorkflowStage_WorkflowStageDefinitionId",
                table: "ProjectTypeWorkflowStage",
                column: "WorkflowStageDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskResultDefinition_Code",
                table: "TaskResultDefinition",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignment_LastTaskResult",
                table: "ProjectAssignment",
                column: "LastTaskResultId",
                principalTable: "TaskResultDefinition",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignmentEvent_TaskResult",
                table: "ProjectAssignmentEvent",
                column: "TaskResultId",
                principalTable: "TaskResultDefinition",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignment_LastTaskResult",
                table: "ProjectAssignment");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignmentEvent_TaskResult",
                table: "ProjectAssignmentEvent");

            migrationBuilder.DropTable(
                name: "ProjectTypeDiscipline");

            migrationBuilder.DropTable(
                name: "ProjectTypeWorkflowStage");

            migrationBuilder.DropTable(
                name: "TaskResultDefinition");

            migrationBuilder.DropIndex(
                name: "IX_ProjectStatus_Code",
                table: "ProjectStatus");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignmentStatus_Code",
                table: "ProjectAssignmentStatus");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignmentEvent_TaskResultId",
                table: "ProjectAssignmentEvent");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_LastTaskResultId",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ProjectStatus");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProjectStatus");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ProjectStatus");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ProjectAssignmentStatus");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProjectAssignmentStatus");

            migrationBuilder.DropColumn(
                name: "TaskResultId",
                table: "ProjectAssignmentEvent");

            migrationBuilder.DropColumn(
                name: "LastTaskResultId",
                table: "ProjectAssignment");
        }
    }
}
