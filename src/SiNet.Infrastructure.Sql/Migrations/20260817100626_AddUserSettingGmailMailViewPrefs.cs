using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettingGmailMailViewPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GmailMailCategory",
                table: "UserSetting",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailMailScope",
                table: "UserSetting",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GmailUnreadOnly",
                table: "UserSetting",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GmailMailCategory",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "GmailMailScope",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "GmailUnreadOnly",
                table: "UserSetting");
        }
    }
}
