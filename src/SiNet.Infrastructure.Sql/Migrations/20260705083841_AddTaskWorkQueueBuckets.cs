using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskWorkQueueBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultWorkQueueBucket",
                table: "TaskType",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkQueueBucket",
                table: "ProjectAssignment",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultWorkQueueBucket",
                table: "TaskType");

            migrationBuilder.DropColumn(
                name: "WorkQueueBucket",
                table: "ProjectAssignment");
        }
    }
}
