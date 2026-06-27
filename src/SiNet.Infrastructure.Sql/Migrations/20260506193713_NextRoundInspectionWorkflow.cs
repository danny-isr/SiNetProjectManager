using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class NextRoundInspectionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLockedAfterSend",
                table: "InspectionReports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSnapshotAt",
                table: "InspectionReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "InspectionReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SentByUserId",
                table: "InspectionReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentSpreadsheetId",
                table: "InspectionReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentSpreadsheetUrl",
                table: "InspectionReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccFileName",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccFileUrn",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccFileVersionUrn",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccIssueId",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccIssueUrl",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccLinkedItemType",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccMarkupId",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccMarkupUrl",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccProjectId",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OurResponseToPlanner",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseAttachmentFileId",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseAttachmentUrl",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannerResponseImportedAt",
                table: "InspectionNotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannerResponseReceivedAt",
                table: "InspectionNotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceType",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseSourceUrl",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerResponseText",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseReviewStatus",
                table: "InspectionNotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InspectionNoteAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoteId = table.Column<long>(type: "bigint", nullable: false),
                    AttachmentType = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleDriveFileId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GoogleDriveUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionNoteAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_InspectionNoteAttachments_InspectionNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "InspectionNotes",
                        principalColumn: "NoteId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionNoteAttachments_SIUser_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "InspectionReportSnapshots",
                columns: table => new
                {
                    SnapshotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    ExportedSpreadsheetId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExportedSpreadsheetUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportNumber = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCurrentSentSnapshot = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionReportSnapshots", x => x.SnapshotId);
                    table.ForeignKey(
                        name: "FK_InspectionReportSnapshots_InspectionReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "InspectionReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionReportSnapshots_SIUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReports_SentByUserId",
                table: "InspectionReports",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNoteAttachments_NoteId",
                table: "InspectionNoteAttachments",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionNoteAttachments_UploadedByUserId",
                table: "InspectionNoteAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportSnapshots_CreatedByUserId",
                table: "InspectionReportSnapshots",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportSnapshots_ReportId",
                table: "InspectionReportSnapshots",
                column: "ReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReports_SIUser_SentByUserId",
                table: "InspectionReports",
                column: "SentByUserId",
                principalTable: "SIUser",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReports_SIUser_SentByUserId",
                table: "InspectionReports");

            migrationBuilder.DropTable(
                name: "InspectionNoteAttachments");

            migrationBuilder.DropTable(
                name: "InspectionReportSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReports_SentByUserId",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "IsLockedAfterSend",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "LastSnapshotAt",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "SentByUserId",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "SentSpreadsheetId",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "SentSpreadsheetUrl",
                table: "InspectionReports");

            migrationBuilder.DropColumn(
                name: "AccFileName",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccFileUrn",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccFileVersionUrn",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccIssueId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccIssueUrl",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccLinkedItemType",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccMarkupId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccMarkupUrl",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "AccProjectId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "OurResponseToPlanner",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseAttachmentFileId",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseAttachmentUrl",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseImportedAt",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseReceivedAt",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceType",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseSourceUrl",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "PlannerResponseText",
                table: "InspectionNotes");

            migrationBuilder.DropColumn(
                name: "ResponseReviewStatus",
                table: "InspectionNotes");
        }
    }
}
