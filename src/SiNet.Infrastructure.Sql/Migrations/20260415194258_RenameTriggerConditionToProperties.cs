using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class RenameTriggerConditionToProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionJson",
                table: "WorkflowStartTrigger");

            migrationBuilder.AddColumn<string>(
                name: "PropertiesJson",
                table: "WorkflowStartTrigger",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertiesJson",
                table: "WorkflowStartTrigger");

            migrationBuilder.AddColumn<string>(
                name: "ConditionJson",
                table: "WorkflowStartTrigger",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
