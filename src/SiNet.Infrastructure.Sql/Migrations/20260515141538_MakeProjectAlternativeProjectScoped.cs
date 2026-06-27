using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class MakeProjectAlternativeProjectScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAlternative_TypeOfProjectInProject",
                table: "ProjectAlternative");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAlternative_TypeProject_Name",
                table: "ProjectAlternative");

            migrationBuilder.RenameColumn(
                name: "TypeOfProjectInProjectID",
                table: "ProjectAlternative",
                newName: "ProjectID");

            migrationBuilder.RenameIndex(
                name: "IX_ProjectAlternative_TypeOfProjectInProjectID",
                table: "ProjectAlternative",
                newName: "IX_ProjectAlternative_ProjectID");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "ProjectAlternative",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "1");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_Project_NormalizedName",
                table: "ProjectAlternative",
                columns: new[] { "ProjectID", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAlternative_Project",
                table: "ProjectAlternative",
                column: "ProjectID",
                principalTable: "Projects",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAlternative_Project",
                table: "ProjectAlternative");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAlternative_Project_NormalizedName",
                table: "ProjectAlternative");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "ProjectAlternative");

            migrationBuilder.RenameColumn(
                name: "ProjectID",
                table: "ProjectAlternative",
                newName: "TypeOfProjectInProjectID");

            migrationBuilder.RenameIndex(
                name: "IX_ProjectAlternative_ProjectID",
                table: "ProjectAlternative",
                newName: "IX_ProjectAlternative_TypeOfProjectInProjectID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_TypeProject_Name",
                table: "ProjectAlternative",
                columns: new[] { "TypeOfProjectInProjectID", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAlternative_TypeOfProjectInProject",
                table: "ProjectAlternative",
                column: "TypeOfProjectInProjectID",
                principalTable: "TypeOfProjectInProject",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
