using System;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StorageDestination",
                table: "ProjectFile",
                type: "int",
                nullable: false,
                defaultValue: 0);

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
                    ProjectFileId = table.Column<int>(type: "int", nullable: false),
                    ProjectAlternativeId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StorageDestination = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SourceType = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SourceEmailAttachmentId = table.Column<int>(type: "int", nullable: true),
                    AccItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFileInstance", x => x.Id);
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxAttachment_ProjectFileInstanceId",
                table: "EmailInboxAttachment",
                column: "ProjectFileInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectAlternativeId",
                table: "ProjectFileInstance",
                column: "ProjectAlternativeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_ProjectFileId",
                table: "ProjectFileInstance",
                column: "ProjectFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_SourceEmailAttachmentId",
                table: "ProjectFileInstance",
                column: "SourceEmailAttachmentId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectFileInstance_File_Alt_Version",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectFileId", "ProjectAlternativeId", "Version" },
                unique: true,
                filter: "[ProjectAlternativeId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFileInstance",
                table: "EmailInboxAttachment",
                column: "ProjectFileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFileInstance",
                table: "EmailInboxAttachment");

            migrationBuilder.DropTable(
                name: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "IX_EmailInboxAttachment_ProjectFileInstanceId",
                table: "EmailInboxAttachment");

            migrationBuilder.DropColumn(
                name: "StorageDestination",
                table: "ProjectFile");

            migrationBuilder.DropColumn(
                name: "ProjectFileInstanceId",
                table: "EmailInboxAttachment");
        }
    }
}
