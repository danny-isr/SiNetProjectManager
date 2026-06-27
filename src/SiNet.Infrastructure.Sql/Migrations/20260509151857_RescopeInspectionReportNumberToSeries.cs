using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class RescopeInspectionReportNumberToSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_Project_Number",
                table: "InspectionReports");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_Project_Number_NoSeries",
                table: "InspectionReports",
                columns: new[] { "ProjectId", "ReportNumber" },
                unique: true,
                filter: "[SeriesId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_Project_Series_Number",
                table: "InspectionReports",
                columns: new[] { "ProjectId", "SeriesId", "ReportNumber" },
                unique: true,
                filter: "[SeriesId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_Project_Number_NoSeries",
                table: "InspectionReports");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_Project_Series_Number",
                table: "InspectionReports");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_Project_Number",
                table: "InspectionReports",
                columns: new[] { "ProjectId", "ReportNumber" },
                unique: true);
        }
    }
}
