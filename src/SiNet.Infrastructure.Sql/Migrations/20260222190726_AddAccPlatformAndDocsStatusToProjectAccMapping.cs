using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAccPlatformAndDocsStatusToProjectAccMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccPlatform",
                table: "ProjectAccMapping",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocsLastCheckedUtc",
                table: "ProjectAccMapping",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocsLastError",
                table: "ProjectAccMapping",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocsStatus",
                table: "ProjectAccMapping",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAccMapping_DocsStatus",
                table: "ProjectAccMapping",
                column: "DocsStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectAccMapping_DocsStatus",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "AccPlatform",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "DocsLastCheckedUtc",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "DocsLastError",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "DocsStatus",
                table: "ProjectAccMapping");
        }
    }
}
