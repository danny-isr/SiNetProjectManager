using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailThreadIdToEmailInboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GmailThreadId",
                table: "EmailInboxMessage",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxMessage_GmailThreadId",
                table: "EmailInboxMessage",
                column: "GmailThreadId",
                filter: "[GmailThreadId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailInboxMessage_GmailThreadId",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "GmailThreadId",
                table: "EmailInboxMessage");
        }
    }
}
