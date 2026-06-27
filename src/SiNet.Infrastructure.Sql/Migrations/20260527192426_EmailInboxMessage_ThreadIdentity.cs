using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class EmailInboxMessage_ThreadIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThreadKey",
                table: "EmailInboxMessage",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ThreadUniqueId",
                table: "EmailInboxMessage",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxMessage_ThreadKey",
                table: "EmailInboxMessage",
                column: "ThreadKey");

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxMessage_ThreadUniqueId",
                table: "EmailInboxMessage",
                column: "ThreadUniqueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailInboxMessage_ThreadKey",
                table: "EmailInboxMessage");

            migrationBuilder.DropIndex(
                name: "IX_EmailInboxMessage_ThreadUniqueId",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "ThreadKey",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "ThreadUniqueId",
                table: "EmailInboxMessage");
        }
    }
}
