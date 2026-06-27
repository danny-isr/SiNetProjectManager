using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class SectionReArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Section_Chapter",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "VersionTimestamp",
                table: "Sections");

            migrationBuilder.RenameColumn(
                name: "SectionTitle",
                table: "Sections",
                newName: "Subtitle");

            migrationBuilder.RenameColumn(
                name: "ChapterId",
                table: "Sections",
                newName: "TitleId");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_ChapterId",
                table: "Sections",
                newName: "IX_Sections_TitleId");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_ActiveByChapter",
                table: "Sections",
                newName: "IX_Sections_ActiveByTitle");

            migrationBuilder.AlterColumn<int>(
                name: "SectionCode",
                table: "Sections",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Sections",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "SectionTitles",
                columns: table => new
                {
                    TitleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChapterId = table.Column<int>(type: "int", nullable: false),
                    TitleName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionTitles", x => x.TitleId);
                    table.ForeignKey(
                        name: "FK_SectionTitle_Chapter",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "ChapterId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Title_Code_Version",
                table: "Sections",
                columns: new[] { "TitleId", "SectionCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SectionTitles_Chapter_Title",
                table: "SectionTitles",
                columns: new[] { "ChapterId", "TitleName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Section_SectionTitle",
                table: "Sections",
                column: "TitleId",
                principalTable: "SectionTitles",
                principalColumn: "TitleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Section_SectionTitle",
                table: "Sections");

            migrationBuilder.DropTable(
                name: "SectionTitles");

            migrationBuilder.DropIndex(
                name: "IX_Sections_Title_Code_Version",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Sections");

            migrationBuilder.RenameColumn(
                name: "TitleId",
                table: "Sections",
                newName: "ChapterId");

            migrationBuilder.RenameColumn(
                name: "Subtitle",
                table: "Sections",
                newName: "SectionTitle");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_TitleId",
                table: "Sections",
                newName: "IX_Sections_ChapterId");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_ActiveByTitle",
                table: "Sections",
                newName: "IX_Sections_ActiveByChapter");

            migrationBuilder.AlterColumn<string>(
                name: "SectionCode",
                table: "Sections",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "VersionTimestamp",
                table: "Sections",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionCode", "VersionTimestamp" },
                unique: true,
                filter: "[SectionCode] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Section_Chapter",
                table: "Sections",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "ChapterId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
