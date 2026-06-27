using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class EmailInboxMessage_InternetMessageId_RequiredUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InReplyTo",
                table: "EmailInboxMessage",
                type: "varchar(998)",
                unicode: false,
                maxLength: 998,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "EmailInboxMessage",
                type: "varchar(998)",
                unicode: false,
                maxLength: 998,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "References",
                table: "EmailInboxMessage",
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_EmailInboxMessage_InternetMessageId",
                table: "EmailInboxMessage",
                column: "InternetMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_EmailInboxMessage_InternetMessageId",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "InReplyTo",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "InternetMessageId",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "References",
                table: "EmailInboxMessage");
        }
    }
}
