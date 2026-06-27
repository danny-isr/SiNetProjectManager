using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLinkSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskLink",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskID = table.Column<int>(type: "int", nullable: false),
                    LinkedEntityType = table.Column<int>(type: "int", nullable: false),
                    LinkedEntityId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLink", x => x.ID);
                    table.ForeignKey(
                        name: "FK_TaskLink_CreatedByUser",
                        column: x => x.CreatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_TaskLink_ProjectAssignment",
                        column: x => x.TaskID,
                        principalTable: "ProjectAssignment",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_CreatedByUserID",
                table: "TaskLink",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_LinkedEntity",
                table: "TaskLink",
                columns: new[] { "LinkedEntityType", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_TaskID",
                table: "TaskLink",
                column: "TaskID");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_Unique",
                table: "TaskLink",
                columns: new[] { "TaskID", "LinkedEntityType", "LinkedEntityId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskLink");
        }
    }
}
