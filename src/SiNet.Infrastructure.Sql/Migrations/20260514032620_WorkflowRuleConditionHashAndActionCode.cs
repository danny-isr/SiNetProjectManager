using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowRuleConditionHashAndActionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowTransitionRule_Unique",
                table: "WorkflowTransitionRule");

            migrationBuilder.AddColumn<string>(
                name: "ConditionHash",
                table: "WorkflowTransitionRule",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActionCode",
                table: "WorkflowTransitionAction",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionRule_Unique",
                table: "WorkflowTransitionRule",
                columns: new[] { "WorkflowDefinitionID", "FromStageID", "ToStageID", "TriggerType", "ConditionType", "ConditionHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowTransitionRule_Unique",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "ConditionHash",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "ActionCode",
                table: "WorkflowTransitionAction");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionRule_Unique",
                table: "WorkflowTransitionRule",
                columns: new[] { "WorkflowDefinitionID", "FromStageID", "ToStageID" },
                unique: true);
        }
    }
}
