using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailInboxTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailInboxMessage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageUniqueId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InboxAccProjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InboxAccFolderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedByLogin = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailInboxMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailInboxMessage_Project",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailInboxAttachment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    AttachmentIndex = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    SavedFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ContentSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    AccItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailInboxAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailInboxAttachment_Message",
                        column: x => x.MessageId,
                        principalTable: "EmailInboxMessage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxAttachment_MessageId",
                table: "EmailInboxAttachment",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "UQ_EmailInboxAttachment_MessageId_ContentSha256",
                table: "EmailInboxAttachment",
                columns: new[] { "MessageId", "ContentSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxMessage_ProjectId",
                table: "EmailInboxMessage",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxMessage_Status_ReceivedUtc",
                table: "EmailInboxMessage",
                columns: new[] { "Status", "ReceivedUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_EmailInboxMessage_MessageUniqueId",
                table: "EmailInboxMessage",
                column: "MessageUniqueId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailInboxAttachment");

            migrationBuilder.DropTable(
                name: "EmailInboxMessage");
        }
    }
}
