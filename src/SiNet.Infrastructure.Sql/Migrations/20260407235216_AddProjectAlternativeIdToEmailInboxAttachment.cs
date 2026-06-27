using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAlternativeIdToEmailInboxAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectAlternativeId",
                table: "EmailInboxAttachment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailInboxAttachment_ProjectAlternativeId",
                table: "EmailInboxAttachment",
                column: "ProjectAlternativeId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailInboxAttachment_ProjectAlternative_ProjectAlternativeId",
                table: "EmailInboxAttachment",
                column: "ProjectAlternativeId",
                principalTable: "ProjectAlternative",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailInboxAttachment_ProjectAlternative_ProjectAlternativeId",
                table: "EmailInboxAttachment");

            migrationBuilder.DropIndex(
                name: "IX_EmailInboxAttachment_ProjectAlternativeId",
                table: "EmailInboxAttachment");

            migrationBuilder.DropColumn(
                name: "ProjectAlternativeId",
                table: "EmailInboxAttachment");
        }
    }
}
