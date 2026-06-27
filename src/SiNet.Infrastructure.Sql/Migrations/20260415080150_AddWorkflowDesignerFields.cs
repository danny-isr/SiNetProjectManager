using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDesignerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "WorkflowTransitionRule",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "WorkflowTransitionRule",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "WorkflowTransitionRule",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RoutePointsJson",
                table: "WorkflowTransitionRule",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CanvasX",
                table: "WorkflowStageDefinition",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CanvasY",
                table: "WorkflowStageDefinition",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "WorkflowStageDefinition",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NodeType",
                table: "WorkflowStageDefinition",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Stage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Condition",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "RoutePointsJson",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "CanvasX",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "CanvasY",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "NodeType",
                table: "WorkflowStageDefinition");
        }
    }
}
