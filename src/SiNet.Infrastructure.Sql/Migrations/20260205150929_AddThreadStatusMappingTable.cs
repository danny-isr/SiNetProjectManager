using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddThreadStatusMappingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThreadStatusMapping",
                columns: table => new
                {
                    ThreadId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BimFolderId = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    GmailLabelId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadStatusMapping", x => x.ThreadId);
                    table.ForeignKey(
                        name: "FK_ThreadStatusMapping_Projects",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_LastUpdated",
                table: "ThreadStatusMapping",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_ProjectId",
                table: "ThreadStatusMapping",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_Status",
                table: "ThreadStatusMapping",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThreadStatusMapping");
        }
    }
}
