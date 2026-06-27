using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class ThreadStatusMapping_PkSurrogateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ThreadStatusMapping",
                table: "ThreadStatusMapping");

            migrationBuilder.DropIndex(
                name: "IX_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping");

            migrationBuilder.AlterColumn<string>(
                name: "ThreadUniqueId",
                table: "ThreadStatusMapping",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "ThreadStatusMapping",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldUnicode: false,
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ThreadStatusMapping",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ThreadStatusMapping",
                table: "ThreadStatusMapping",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_ThreadId",
                table: "ThreadStatusMapping",
                column: "ThreadId",
                filter: "[ThreadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping",
                column: "ThreadUniqueId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ThreadStatusMapping",
                table: "ThreadStatusMapping");

            migrationBuilder.DropIndex(
                name: "IX_ThreadStatusMapping_ThreadId",
                table: "ThreadStatusMapping");

            migrationBuilder.DropIndex(
                name: "UQ_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ThreadStatusMapping");

            migrationBuilder.AlterColumn<string>(
                name: "ThreadUniqueId",
                table: "ThreadStatusMapping",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ThreadId",
                table: "ThreadStatusMapping",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldUnicode: false,
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ThreadStatusMapping",
                table: "ThreadStatusMapping",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping",
                column: "ThreadUniqueId");
        }
    }
}
