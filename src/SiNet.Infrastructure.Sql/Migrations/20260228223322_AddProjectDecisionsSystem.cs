using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDecisionsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionCategory",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionCategory", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDecision",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectID = table.Column<int>(type: "int", nullable: false),
                    CategoryID = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    LastUpdatedByUserID = table.Column<int>(type: "int", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDecision", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ProjectDecision_Category",
                        column: x => x.CategoryID,
                        principalTable: "DecisionCategory",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDecision_CreatedByUser",
                        column: x => x.CreatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDecision_LastUpdatedByUser",
                        column: x => x.LastUpdatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDecision_Project",
                        column: x => x.ProjectID,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DecisionHistory",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DecisionID = table.Column<int>(type: "int", nullable: false),
                    OldContent = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ChangedByUserID = table.Column<int>(type: "int", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionHistory", x => x.ID);
                    table.ForeignKey(
                        name: "FK_DecisionHistory_ChangedByUser",
                        column: x => x.ChangedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionHistory_Decision",
                        column: x => x.DecisionID,
                        principalTable: "ProjectDecision",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionCategory_Name",
                table: "DecisionCategory",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DecisionHistory_ChangedByUserID",
                table: "DecisionHistory",
                column: "ChangedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionHistory_Decision",
                table: "DecisionHistory",
                column: "DecisionID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDecision_CategoryID",
                table: "ProjectDecision",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDecision_CreatedByUserID",
                table: "ProjectDecision",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDecision_LastUpdatedByUserID",
                table: "ProjectDecision",
                column: "LastUpdatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDecision_Project",
                table: "ProjectDecision",
                column: "ProjectID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionHistory");

            migrationBuilder.DropTable(
                name: "ProjectDecision");

            migrationBuilder.DropTable(
                name: "DecisionCategory");
        }
    }
}
