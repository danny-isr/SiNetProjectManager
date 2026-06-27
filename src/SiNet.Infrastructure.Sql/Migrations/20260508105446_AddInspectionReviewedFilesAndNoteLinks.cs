using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionReviewedFilesAndNoteLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewedVersion",
                table: "InspectionReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedAlternative",
                table: "InspectionNotes",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedFileName",
                table: "InspectionNotes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedVersion",
                table: "InspectionNotes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InspectionReportReviewedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Alternative = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionReportReviewedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionReportReviewedFile_Report",
                        column: x => x.ReportId,
                        principalTable: "InspectionReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportReviewedFiles_Report_File_Alt",
                table: "InspectionReportReviewedFiles",
                columns: new[] { "ReportId", "FileName", "Alternative" },
                unique: true,
                filter: "[Alternative] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportReviewedFiles_ReportId",
                table: "InspectionReportReviewedFiles",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionReportReviewedFiles");

            migrationBuilder.DropColumn(
                name: "ReviewedVersion",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "LinkedAlternative",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "LinkedFileName",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "LinkedVersion",
                table: "InspectionNotes");
        }
    }
}
