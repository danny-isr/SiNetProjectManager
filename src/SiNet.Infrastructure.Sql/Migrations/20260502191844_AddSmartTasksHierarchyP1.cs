using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartTasksHierarchyP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectAssignment_ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_UniqueTask",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "ProjectAssignment_TitleIndex",
                table: "ProjectAssignment");

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredForParentCompletion",
                table: "ProjectAssignment",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentAssignmentID",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrderInParent",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_ParentAssignmentID",
                table: "ProjectAssignment",
                column: "ParentAssignmentID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_Title",
                table: "ProjectAssignment",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_UniqueOpenTask",
                table: "ProjectAssignment",
                columns: new[] { "ProjectID", "AssignedToID", "TaskTypeID", "ParentAssignmentID" },
                unique: true,
                filter: "[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL AND [WorkPriority] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignment_ParentAssignment",
                table: "ProjectAssignment",
                column: "ParentAssignmentID",
                principalTable: "ProjectAssignment",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignment_ParentAssignment",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_ParentAssignmentID",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_Title",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_UniqueOpenTask",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "IsRequiredForParentCompletion",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "ParentAssignmentID",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "SortOrderInParent",
                table: "ProjectAssignment");

            migrationBuilder.CreateTable(
                name: "ProjectAssignment_ProjectAssignment",
                columns: table => new
                {
                    ProjectAssignmentID = table.Column<int>(type: "int", nullable: false),
                    ProjectAssignmentID1 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ProjectAssignment_ProjectAssignment_Index", x => new { x.ProjectAssignmentID, x.ProjectAssignmentID1 });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_UniqueTask",
                table: "ProjectAssignment",
                columns: new[] { "ProjectID", "AssignedToID", "TaskTypeID" },
                unique: true,
                filter: "[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ProjectAssignment_TitleIndex",
                table: "ProjectAssignment",
                column: "Title",
                unique: true,
                filter: "[Title] IS NOT NULL");
        }
    }
}
