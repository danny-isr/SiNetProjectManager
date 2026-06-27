using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectFileInstanceTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFileInstance",
                table: "EmailInboxAttachment");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReportDrawing_FileInstance",
                table: "InspectionReportDrawings");

            migrationBuilder.DropTable(
                name: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "IX_InspectionReportDrawings_FileInstanceId",
                table: "InspectionReportDrawings");

            migrationBuilder.DropIndex(
                name: "IX_EmailInboxAttachment_ProjectFileInstanceId",
                table: "EmailInboxAttachment");

            migrationBuilder.DropColumn(
                name: "FileInstanceId",
                table: "InspectionReportDrawings");

            migrationBuilder.DropColumn(
                name: "ProjectFileInstanceId",
                table: "EmailInboxAttachment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileInstanceId",
                table: "InspectionReportDrawings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectFileInstanceId",
                table: "EmailInboxAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectFileInstance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectAlternativeId = table.Column<int>(type: "int", nullable: true),
                    ProjectFileId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    SourceEmailAttachmentId = table.Column<int>(type: "int", nullable: true),
                    SupersededByProjectFileInstanceId = table.Column<int>(type: "int", nullable: true),
                    AccFolderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceType = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    StorageDestination = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFileInstance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectFileInstance_Project",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectFileInstance_ProjectAlternative",
                        column: x => x.ProjectAlternativeId,
                        principalTable: "ProjectAlternative",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectFileInstance_ProjectFile",
                        column: x => x.ProjectFileId,
                        principalTable: "ProjectFile",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectFileInstance_SourceEmailAttachment",
                        column: x => x.SourceEmailAttachmentId,
                        principalTable: "EmailInboxAttachment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectFileInstance_SupersededBy",
                        column: x => x.SupersededByProjectFileInstanceId,
                        principalTable: "ProjectFileInstance",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReportDrawings_FileInstanceId",
                table: "InspectionReportDrawings",
                column: "FileInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxAttachment_ProjectFileInstanceId",
                table: "EmailInboxAttachment",
                column: "ProjectFileInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_IsActive_ProjectId",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectAlternativeId",
                table: "ProjectFileInstance",
                column: "ProjectAlternativeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectFileId",
                table: "ProjectFileInstance",
                column: "ProjectFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectId",
                table: "ProjectFileInstance",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_SourceEmailAttachmentId",
                table: "ProjectFileInstance",
                column: "SourceEmailAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_SupersededByProjectFileInstanceId",
                table: "ProjectFileInstance",
                column: "SupersededByProjectFileInstanceId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectFileInstance_Proj_File_Alt_Version",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectId", "ProjectFileId", "ProjectAlternativeId", "Version" },
                unique: true,
                filter: "[ProjectAlternativeId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFileInstance",
                table: "EmailInboxAttachment",
                column: "ProjectFileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReportDrawing_FileInstance",
                table: "InspectionReportDrawings",
                column: "FileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
