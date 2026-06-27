using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailInboxProcessingLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingByLogin",
                table: "EmailInboxMessage",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAtUtc",
                table: "EmailInboxMessage",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingByLogin",
                table: "EmailInboxMessage");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAtUtc",
                table: "EmailInboxMessage");
        }
    }
}
