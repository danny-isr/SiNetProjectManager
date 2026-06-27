using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddActionPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionPermission",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActionDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionPermission", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ActionPermission_SIUser_UserId",
                        column: x => x.UserId,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionPermission_ActionCode",
                table: "ActionPermission",
                column: "ActionCode");

            migrationBuilder.CreateIndex(
                name: "IX_ActionPermission_ActionCode_UserId",
                table: "ActionPermission",
                columns: new[] { "ActionCode", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionPermission_UserId",
                table: "ActionPermission",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionPermission");
        }
    }
}
