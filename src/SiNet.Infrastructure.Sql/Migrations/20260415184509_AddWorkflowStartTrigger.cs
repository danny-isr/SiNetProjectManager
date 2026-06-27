using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStartTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowStartTrigger",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionID = table.Column<int>(type: "int", nullable: false),
                    TriggerSource = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ConditionJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ParameterMappingJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStartTrigger", x => x.ID);
                    table.ForeignKey(
                        name: "FK_WorkflowStartTrigger_Definition",
                        column: x => x.WorkflowDefinitionID,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStartTrigger_DefSort",
                table: "WorkflowStartTrigger",
                columns: new[] { "WorkflowDefinitionID", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStartTrigger_SourceActive",
                table: "WorkflowStartTrigger",
                columns: new[] { "TriggerSource", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStartTrigger");
        }
    }
}
