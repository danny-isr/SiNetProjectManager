using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdToProjectFileInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_ProjectFileInstance_File_Alt_Version",
                table: "ProjectFileInstance");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "ProjectFileInstance",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectId",
                table: "ProjectFileInstance",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectFileInstance_Proj_File_Alt_Version",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectId", "ProjectFileId", "ProjectAlternativeId", "Version" },
                unique: true,
                filter: "[ProjectAlternativeId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFileInstance_Project",
                table: "ProjectFileInstance",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectFileInstance_Project",
                table: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "IX_ProjectFileInstance_ProjectId",
                table: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "UQ_ProjectFileInstance_Proj_File_Alt_Version",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ProjectFileInstance");

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectFileInstance_File_Alt_Version",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectFileId", "ProjectAlternativeId", "Version" },
                unique: true,
                filter: "[ProjectAlternativeId] IS NOT NULL");
        }
    }
}
