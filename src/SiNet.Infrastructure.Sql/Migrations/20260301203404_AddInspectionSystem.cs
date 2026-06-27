using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    ChapterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChapterNumber = table.Column<int>(type: "int", nullable: false),
                    ChapterTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.ChapterId);
                });

            migrationBuilder.CreateTable(
                name: "InspectionReports",
                columns: table => new
                {
                    ReportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ReportNumber = table.Column<int>(type: "int", nullable: false),
                    InspectionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectorName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SourceFileUrn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceFileVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionReports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_InspectionReport_Project",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    SectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChapterId = table.Column<int>(type: "int", nullable: false),
                    SectionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SectionTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.SectionId);
                    table.ForeignKey(
                        name: "FK_Section_Chapter",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "ChapterId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommentsBank",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    CommonText = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentsBank", x => x.CommentId);
                    table.ForeignKey(
                        name: "FK_CommentsBank_Section",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionNotes",
                columns: table => new
                {
                    NoteId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    NoteSubIndex = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NoteText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoteStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccMarkupLink = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreviousNoteId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionNotes", x => x.NoteId);
                    table.ForeignKey(
                        name: "FK_InspectionNote_PreviousNote",
                        column: x => x.PreviousNoteId,
                        principalTable: "InspectionNotes",
                        principalColumn: "NoteId");
                    table.ForeignKey(
                        name: "FK_InspectionNote_Report",
                        column: x => x.ReportId,
                        principalTable: "InspectionReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionNote_Section",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ChapterNumber",
                table: "Chapters",
                column: "ChapterNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentsBank_SectionId",
                table: "CommentsBank",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNotes_PreviousNoteId",
                table: "InspectionNotes",
                column: "PreviousNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNotes_ReportId",
                table: "InspectionNotes",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNotes_SectionId",
                table: "InspectionNotes",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_ProjectId",
                table: "InspectionReports",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ChapterId",
                table: "Sections",
                column: "ChapterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentsBank");

            migrationBuilder.DropTable(
                name: "InspectionNotes");

            migrationBuilder.DropTable(
                name: "InspectionReports");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Chapters");
        }
    }
}
