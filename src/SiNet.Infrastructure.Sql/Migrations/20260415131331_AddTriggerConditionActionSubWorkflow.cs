using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggerConditionActionSubWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionJson",
                table: "WorkflowTransitionRule",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConditionType",
                table: "WorkflowTransitionRule",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EvaluationMode",
                table: "WorkflowTransitionRule",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TriggerType",
                table: "WorkflowTransitionRule",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubWorkflowDefinitionID",
                table: "WorkflowStageDefinition",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubWorkflowWaitMode",
                table: "WorkflowStageDefinition",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionAction",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransitionRuleID = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitionAction", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionAction_Rule",
                        column: x => x.TransitionRuleID,
                        principalTable: "WorkflowTransitionRule",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageDefinition_SubWorkflowDefinitionID",
                table: "WorkflowStageDefinition",
                column: "SubWorkflowDefinitionID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionAction_RuleSort",
                table: "WorkflowTransitionAction",
                columns: new[] { "TransitionRuleID", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowStageDefinition_SubWorkflow",
                table: "WorkflowStageDefinition",
                column: "SubWorkflowDefinitionID",
                principalTable: "WorkflowDefinition",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowStageDefinition_SubWorkflow",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropTable(
                name: "WorkflowTransitionAction");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowStageDefinition_SubWorkflowDefinitionID",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "ConditionJson",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "ConditionType",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "EvaluationMode",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "TriggerType",
                table: "WorkflowTransitionRule");

            migrationBuilder.DropColumn(
                name: "SubWorkflowDefinitionID",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropColumn(
                name: "SubWorkflowWaitMode",
                table: "WorkflowStageDefinition");
        }
    }
}
