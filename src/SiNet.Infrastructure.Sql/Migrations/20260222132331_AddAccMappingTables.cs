using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAccMappingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccHub",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HubId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccHub", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccSystemResource",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccHubId = table.Column<int>(type: "int", nullable: false),
                    AccProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccRootFolderId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccInboxFolderId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccSystemResource", x => x.Key);
                    table.ForeignKey(
                        name: "FK_AccSystemResource_AccHub",
                        column: x => x.AccHubId,
                        principalTable: "AccHub",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAccMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    AccHubId = table.Column<int>(type: "int", nullable: false),
                    AccProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccTargetFolderId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccTargetFolderPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastVerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAccMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectAccMapping_AccHub",
                        column: x => x.AccHubId,
                        principalTable: "AccHub",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectAccMapping_Project",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccHub_IsDefault",
                table: "AccHub",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "UQ_AccHub_HubId",
                table: "AccHub",
                column: "HubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccSystemResource_AccHubId",
                table: "AccSystemResource",
                column: "AccHubId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAccMapping_AccHubId",
                table: "ProjectAccMapping",
                column: "AccHubId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAccMapping_AccProjectId",
                table: "ProjectAccMapping",
                column: "AccProjectId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectAccMapping_ProjectId",
                table: "ProjectAccMapping",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccSystemResource");

            migrationBuilder.DropTable(
                name: "ProjectAccMapping");

            migrationBuilder.DropTable(
                name: "AccHub");
        }
    }
}
