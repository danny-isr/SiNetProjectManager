using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InspectorId",
                table: "InspectionReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "InspectionReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoteStatusId",
                table: "InspectionNotes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InspectionNoteStatuses",
                columns: table => new
                {
                    StatusId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatusKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HebrewLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ExportSymbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionNoteStatuses", x => x.StatusId);
                });

            migrationBuilder.CreateTable(
                name: "InspectionSeries",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    SeriesName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TemplateUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TemplateSpreadsheetId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionSeries", x => x.SeriesId);
                    table.ForeignKey(
                        name: "FK_InspectionSeries_Project",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "InspectionNoteStatuses",
                columns: new[] { "StatusId", "ExportSymbol", "HebrewLabel", "IsActive", "SortOrder", "StatusKey" },
                values: new object[,]
                {
                    { 1, "V", "מקובל", true, 1, "Passed" },
                    { 2, "X", "הערה", true, 2, "Failed" },
                    { 3, "!", "הערה חוזרת", true, 3, "RecurringFailed" },
                    { 4, "—", "לא רלוונטי", true, 4, "NotApplicable" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_InspectorId",
                table: "InspectionReports",
                column: "InspectorId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_SeriesId",
                table: "InspectionReports",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNotes_NoteStatusId",
                table: "InspectionNotes",
                column: "NoteStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNoteStatuses_StatusKey",
                table: "InspectionNoteStatuses",
                column: "StatusKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeries_ProjectId",
                table: "InspectionSeries",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionNote_NoteStatus",
                table: "InspectionNotes",
                column: "NoteStatusId",
                principalTable: "InspectionNoteStatuses",
                principalColumn: "StatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReport_Inspector",
                table: "InspectionReports",
                column: "InspectorId",
                principalTable: "SIUser",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReport_Series",
                table: "InspectionReports",
                column: "SeriesId",
                principalTable: "InspectionSeries",
                principalColumn: "SeriesId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionNote_NoteStatus",
                table: "InspectionNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReport_Inspector",
                table: "InspectionReports");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReport_Series",
                table: "InspectionReports");

            migrationBuilder.DropTable(
                name: "InspectionNoteStatuses");

            migrationBuilder.DropTable(
                name: "InspectionSeries");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_InspectorId",
                table: "InspectionReports");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_SeriesId",
                table: "InspectionReports");

            migrationBuilder.DropIndex(
                name: "IX_InspectionNotes_NoteStatusId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "InspectorId",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "NoteStatusId",
                table: "InspectionNotes");
        }
    }
}
