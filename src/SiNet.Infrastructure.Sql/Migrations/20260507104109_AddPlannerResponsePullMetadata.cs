using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerResponsePullMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlannerResponsePulledAt",
                table: "InspectionNotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceCell",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannerResponseSourceRow",
                table: "InspectionNotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceSheetName",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceSpreadsheetId",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceSpreadsheetUrl",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannerResponsePulledAt",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceCell",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceRow",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceSheetName",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceSpreadsheetId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceSpreadsheetUrl",
                table: "InspectionNotes");
        }
    }
}
