using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_SyncRunFailures_Table : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Sync_RunFailures",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorStage = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sync_RunFailures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sync_RunFailures_FailedAt",
                schema: "dbo",
                table: "Sync_RunFailures",
                column: "FailedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Sync_RunFailures_RunId",
                schema: "dbo",
                table: "Sync_RunFailures",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sync_RunFailures",
                schema: "dbo");
        }
    }
}
