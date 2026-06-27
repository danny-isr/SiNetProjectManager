using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileTagToEmailInboxAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectFileId",
                table: "EmailInboxAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxAttachment_ProjectFileId",
                table: "EmailInboxAttachment",
                column: "ProjectFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFile",
                table: "EmailInboxAttachment",
                column: "ProjectFileId",
                principalTable: "ProjectFile",
                principalColumn: "ID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailInboxAttachment_ProjectFile",
                table: "EmailInboxAttachment");

            migrationBuilder.DropIndex(
                name: "IX_EmailInboxAttachment_ProjectFileId",
                table: "EmailInboxAttachment");

            migrationBuilder.DropColumn(
                name: "ProjectFileId",
                table: "EmailInboxAttachment");
        }
    }
}
