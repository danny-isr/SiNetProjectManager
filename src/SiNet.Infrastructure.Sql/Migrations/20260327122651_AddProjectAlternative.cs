using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAlternative : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectAlternative",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeOfProjectInProjectID = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Hebrew_100_CI_AS"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    FolderPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedFromFolderScan = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAlternative", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ProjectAlternative_CreatedByUser",
                        column: x => x.CreatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAlternative_TypeOfProjectInProject",
                        column: x => x.TypeOfProjectInProjectID,
                        principalTable: "TypeOfProjectInProject",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectAlternative_UpdatedByUser",
                        column: x => x.UpdatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_CreatedByUserID",
                table: "ProjectAlternative",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_IsActive",
                table: "ProjectAlternative",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_IsPrimary",
                table: "ProjectAlternative",
                column: "IsPrimary");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_TypeOfProjectInProjectID",
                table: "ProjectAlternative",
                column: "TypeOfProjectInProjectID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_TypeProject_Name",
                table: "ProjectAlternative",
                columns: new[] { "TypeOfProjectInProjectID", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAlternative_UpdatedByUserID",
                table: "ProjectAlternative",
                column: "UpdatedByUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectAlternative");
        }
    }
}
