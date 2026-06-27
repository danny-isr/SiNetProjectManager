using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddHybridStatusColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultColorHex",
                table: "ProjectAssignmentStatus",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserStatusPreference",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SIUserID = table.Column<int>(type: "int", nullable: false),
                    StatusID = table.Column<int>(type: "int", nullable: false),
                    OverrideColorHex = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStatusPreference", x => x.ID);
                    table.ForeignKey(
                        name: "FK_UserStatusPreference_ProjectAssignmentStatus",
                        column: x => x.StatusID,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserStatusPreference_SIUser",
                        column: x => x.SIUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserStatusPreference_StatusID",
                table: "UserStatusPreference",
                column: "StatusID");

            migrationBuilder.CreateIndex(
                name: "IX_UserStatusPreference_User_Status",
                table: "UserStatusPreference",
                columns: new[] { "SIUserID", "StatusID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserStatusPreference");

            migrationBuilder.DropColumn(
                name: "DefaultColorHex",
                table: "ProjectAssignmentStatus");
        }
    }
}
