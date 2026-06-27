using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Sections",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VersionTimestamp",
                table: "Sections",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ActiveByChapter",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionCode" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionCode", "VersionTimestamp" },
                unique: true,
                filter: "[SectionCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sections_ActiveByChapter",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "VersionTimestamp",
                table: "Sections");
        }
    }
}
