using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionDrawings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionReportDrawings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    FileInstanceId = table.Column<int>(type: "int", nullable: true),
                    SourceFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileType = table.Column<int>(type: "int", nullable: false),
                    SelectedLayoutIndices = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StampStatus = table.Column<int>(type: "int", nullable: false),
                    StampedFilePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StampedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionReportDrawings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionReportDrawings_InspectionReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "InspectionReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionReportDrawings_ProjectFileInstance_FileInstanceId",
                        column: x => x.FileInstanceId,
                        principalTable: "ProjectFileInstance",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InspectionSeriesFileConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeriesId = table.Column<int>(type: "int", nullable: false),
                    ProjectFileId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionSeriesFileConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionSeriesFileConfigs_InspectionSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "InspectionSeries",
                        principalColumn: "SeriesId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionSeriesFileConfigs_ProjectFile_ProjectFileId",
                        column: x => x.ProjectFileId,
                        principalTable: "ProjectFile",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportDrawings_FileInstanceId",
                table: "InspectionReportDrawings",
                column: "FileInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportDrawings_ReportId",
                table: "InspectionReportDrawings",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeriesFileConfigs_ProjectFileId",
                table: "InspectionSeriesFileConfigs",
                column: "ProjectFileId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeriesFileConfigs_SeriesId",
                table: "InspectionSeriesFileConfigs",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionReportDrawings");

            migrationBuilder.DropTable(
                name: "InspectionSeriesFileConfigs");
        }
    }
}
