using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTypeTaskRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectTypeStatus",
                columns: table => new
                {
                    ProjectTypeID = table.Column<int>(type: "int", nullable: false),
                    StatusID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTypeStatus", x => new { x.ProjectTypeID, x.StatusID });
                    table.ForeignKey(
                        name: "FK_ProjectTypeStatus_JobType",
                        column: x => x.ProjectTypeID,
                        principalTable: "JobType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTypeStatus_ProjectAssignmentStatus",
                        column: x => x.StatusID,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTypeTaskType",
                columns: table => new
                {
                    ProjectTypeID = table.Column<int>(type: "int", nullable: false),
                    TaskTypeID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTypeTaskType", x => new { x.ProjectTypeID, x.TaskTypeID });
                    table.ForeignKey(
                        name: "FK_ProjectTypeTaskType_JobType",
                        column: x => x.ProjectTypeID,
                        principalTable: "JobType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTypeTaskType_TaskType",
                        column: x => x.TaskTypeID,
                        principalTable: "TaskType",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeStatus_StatusID",
                table: "ProjectTypeStatus",
                column: "StatusID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeStatus_Unique",
                table: "ProjectTypeStatus",
                columns: new[] { "ProjectTypeID", "StatusID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeTaskType_TaskTypeID",
                table: "ProjectTypeTaskType",
                column: "TaskTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTypeTaskType_Unique",
                table: "ProjectTypeTaskType",
                columns: new[] { "ProjectTypeID", "TaskTypeID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectTypeStatus");

            migrationBuilder.DropTable(
                name: "ProjectTypeTaskType");
        }
    }
}
