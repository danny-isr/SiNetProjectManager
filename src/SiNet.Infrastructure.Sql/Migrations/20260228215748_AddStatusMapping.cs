using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskStatusToProjectStatusMapping",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskStatusID = table.Column<int>(type: "int", nullable: false),
                    ProjectStatusID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskStatusToProjectStatusMapping", x => x.ID);
                    table.ForeignKey(
                        name: "FK_TaskStatusProjectStatusMapping_ProjectStatus",
                        column: x => x.ProjectStatusID,
                        principalTable: "ProjectStatus",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskStatusProjectStatusMapping_TaskStatus",
                        column: x => x.TaskStatusID,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskStatusToProjectStatusMapping_ProjectStatusID",
                table: "TaskStatusToProjectStatusMapping",
                column: "ProjectStatusID");

            migrationBuilder.CreateIndex(
                name: "IX_TaskStatusToProjectStatusMapping_TaskStatus",
                table: "TaskStatusToProjectStatusMapping",
                column: "TaskStatusID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskStatusToProjectStatusMapping");
        }
    }
}
