using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class DictionaryBasedRefactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Section_SectionTitle",
                table: "Sections");

            migrationBuilder.DropTable(
                name: "SectionTitles");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ActiveByTitle",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_Title_Code_Version",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_ChapterNumber",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "ChapterTitle",
                table: "Chapters");

            migrationBuilder.RenameColumn(
                name: "TitleId",
                table: "Sections",
                newName: "SectionNameId");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_TitleId",
                table: "Sections",
                newName: "IX_Sections_SectionNameId");

            migrationBuilder.AddColumn<int>(
                name: "ChapterId",
                table: "Sections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChapterNameId",
                table: "Chapters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Chapters",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChapterNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterNames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SectionNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionNames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ActiveByChapter",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionCode" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ActiveChapterName",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionNameId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections",
                columns: new[] { "ChapterId", "SectionCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ChapterId",
                table: "Sections",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeries_Project_Name",
                table: "InspectionSeries",
                columns: new[] { "ProjectId", "SeriesName" },
                unique: true,
                filter: "[SeriesName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries",
                columns: new[] { "ProjectId", "TemplateSpreadsheetId" },
                unique: true,
                filter: "[TemplateSpreadsheetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_Project_Number",
                table: "InspectionReports",
                columns: new[] { "ProjectId", "ReportNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNotes_Report_Section_SubIndex",
                table: "InspectionNotes",
                columns: new[] { "ReportId", "SectionId", "NoteSubIndex" },
                unique: true,
                filter: "[NoteSubIndex] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ChapterNameId",
                table: "Chapters",
                column: "ChapterNameId");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_Series_Number",
                table: "Chapters",
                columns: new[] { "SeriesId", "ChapterNumber" },
                unique: true,
                filter: "[SeriesId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChapterNames_Name",
                table: "ChapterNames",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SectionNames_Name",
                table: "SectionNames",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Chapter_ChapterName",
                table: "Chapters",
                column: "ChapterNameId",
                principalTable: "ChapterNames",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Chapter_Series",
                table: "Chapters",
                column: "SeriesId",
                principalTable: "InspectionSeries",
                principalColumn: "SeriesId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Section_Chapter",
                table: "Sections",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "ChapterId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Section_SectionName",
                table: "Sections",
                column: "SectionNameId",
                principalTable: "SectionNames",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chapter_ChapterName",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Chapter_Series",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Section_Chapter",
                table: "Sections");

            migrationBuilder.DropForeignKey(
                name: "FK_Section_SectionName",
                table: "Sections");

            migrationBuilder.DropTable(
                name: "ChapterNames");

            migrationBuilder.DropTable(
                name: "SectionNames");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ActiveByChapter",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ActiveChapterName",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_Chapter_Code_Version",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ChapterId",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_InspectionSeries_Project_Name",
                table: "InspectionSeries");

            migrationBuilder.DropIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_Project_Number",
                table: "InspectionReports");

            migrationBuilder.DropIndex(
                name: "IX_InspectionNotes_Report_Section_SubIndex",
                table: "InspectionNotes");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_ChapterNameId",
                table: "Chapters");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_Series_Number",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "ChapterId",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "ChapterNameId",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Chapters");

            migrationBuilder.RenameColumn(
                name: "SectionNameId",
                table: "Sections",
                newName: "TitleId");

            migrationBuilder.RenameIndex(
                name: "IX_Sections_SectionNameId",
                table: "Sections",
                newName: "IX_Sections_TitleId");

            migrationBuilder.AddColumn<string>(
                name: "ChapterTitle",
                table: "Chapters",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

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
                name: "IX_Sections_ActiveByTitle",
                table: "Sections",
                columns: new[] { "TitleId", "SectionCode" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_Title_Code_Version",
                table: "Sections",
                columns: new[] { "TitleId", "SectionCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ChapterNumber",
                table: "Chapters",
                column: "ChapterNumber",
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
    }
}
