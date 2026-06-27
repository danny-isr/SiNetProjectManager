using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class DropUniqueSeriesTemplateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries",
                columns: new[] { "ProjectId", "TemplateSpreadsheetId" },
                filter: "[TemplateSpreadsheetId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeries_Project_Template",
                table: "InspectionSeries",
                columns: new[] { "ProjectId", "TemplateSpreadsheetId" },
                unique: true,
                filter: "[TemplateSpreadsheetId] IS NOT NULL");
        }
    }
}
