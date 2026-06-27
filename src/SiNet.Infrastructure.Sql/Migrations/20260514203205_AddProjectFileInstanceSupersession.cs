using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFileInstanceSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "ProjectFileInstance",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ProjectFileInstance",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProjectFileInstance",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "ProjectFileInstance",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersededByProjectFileInstanceId",
                table: "ProjectFileInstance",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_IsActive_ProjectId",
                table: "ProjectFileInstance",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFileInstance_SupersededByProjectFileInstanceId",
                table: "ProjectFileInstance",
                column: "SupersededByProjectFileInstanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFileInstance_SupersededBy",
                table: "ProjectFileInstance",
                column: "SupersededByProjectFileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectFileInstance_SupersededBy",
                table: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "IX_ProjectFileInstance_IsActive_ProjectId",
                table: "ProjectFileInstance");

            migrationBuilder.DropIndex(
                name: "IX_ProjectFileInstance_SupersededByProjectFileInstanceId",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                table: "ProjectFileInstance");

            migrationBuilder.DropColumn(
                name: "SupersededByProjectFileInstanceId",
                table: "ProjectFileInstance");
        }
    }
}
