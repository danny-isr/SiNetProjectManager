using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLinkWorkTargetsP2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "TaskLink",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletedByUserID",
                table: "TaskLink",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWorkTarget",
                table: "TaskLink",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WorkStatus",
                table: "TaskLink",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AggregationMode",
                table: "TaskBehaviorDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TaskLink_WorkTarget",
                table: "TaskLink",
                columns: new[] { "TaskID", "IsWorkTarget", "WorkStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskLink_WorkTarget",
                table: "TaskLink");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "TaskLink");

            migrationBuilder.DropColumn(
                name: "CompletedByUserID",
                table: "TaskLink");

            migrationBuilder.DropColumn(
                name: "IsWorkTarget",
                table: "TaskLink");

            migrationBuilder.DropColumn(
                name: "WorkStatus",
                table: "TaskLink");

            migrationBuilder.DropColumn(
                name: "AggregationMode",
                table: "TaskBehaviorDefinitions");
        }
    }
}
