using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AccMemberSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MembersHashLocal",
                table: "ProjectAccMapping",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MembersLastError",
                table: "ProjectAccMapping",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MembersLastSyncedUtc",
                table: "ProjectAccMapping",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MembersHashLocal",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "MembersLastError",
                table: "ProjectAccMapping");

            migrationBuilder.DropColumn(
                name: "MembersLastSyncedUtc",
                table: "ProjectAccMapping");
        }
    }
}
