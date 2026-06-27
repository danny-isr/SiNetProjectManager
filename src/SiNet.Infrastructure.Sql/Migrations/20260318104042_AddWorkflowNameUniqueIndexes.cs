using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNameUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the stable machine identifier column to TaskType
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "TaskType",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "CAST(NEWID() AS NVARCHAR(50))");

            migrationBuilder.CreateIndex(
                name: "IX_TaskType_Code",
                table: "TaskType",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageDefinition_DefName",
                table: "WorkflowStageDefinition",
                columns: new[] { "WorkflowDefinitionID", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinition_Name",
                table: "WorkflowDefinition",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowStageDefinition_DefName",
                table: "WorkflowStageDefinition");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinition_Name",
                table: "WorkflowDefinition");

            migrationBuilder.DropIndex(
                name: "IX_TaskType_Code",
                table: "TaskType");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "TaskType");
        }
    }
}
