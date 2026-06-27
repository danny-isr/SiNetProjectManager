using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProjectForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ThreadStatusMapping_Projects",
                table: "ThreadStatusMapping");

            migrationBuilder.AlterColumn<int>(
                name: "ProjectId",
                table: "ThreadStatusMapping",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ThreadStatusMapping_Projects",
                table: "ThreadStatusMapping",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ThreadStatusMapping_Projects",
                table: "ThreadStatusMapping");

            migrationBuilder.AlterColumn<int>(
                name: "ProjectId",
                table: "ThreadStatusMapping",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_ThreadStatusMapping_Projects",
                table: "ThreadStatusMapping",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "ID",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
