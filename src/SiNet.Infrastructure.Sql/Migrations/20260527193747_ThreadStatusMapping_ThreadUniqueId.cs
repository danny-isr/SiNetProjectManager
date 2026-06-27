using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class ThreadStatusMapping_ThreadUniqueId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThreadUniqueId",
                table: "ThreadStatusMapping",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping",
                column: "ThreadUniqueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ThreadStatusMapping_ThreadUniqueId",
                table: "ThreadStatusMapping");

            migrationBuilder.DropColumn(
                name: "ThreadUniqueId",
                table: "ThreadStatusMapping");
        }
    }
}
