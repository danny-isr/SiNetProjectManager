using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskManagement_v2_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StatusID",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaskTypeID",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkPriority",
                table: "ProjectAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectAssignmentStatus",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Hebrew_100_CI_AS"),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAssignmentStatus", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "TaskType",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, collation: "Hebrew_100_CI_AS"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskType", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "UserSetting",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SIUserID = table.Column<int>(type: "int", nullable: false),
                    AutoOpenTasksPanelAfterFiling = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSetting", x => x.ID);
                    table.ForeignKey(
                        name: "FK_UserSetting_SIUser",
                        column: x => x.SIUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAssignmentEvent",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectAssignmentID = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Hebrew_100_CI_AS"),
                    OldStatusID = table.Column<int>(type: "int", nullable: true),
                    NewStatusID = table.Column<int>(type: "int", nullable: true),
                    ContactID = table.Column<int>(type: "int", nullable: true),
                    CompanyID = table.Column<int>(type: "int", nullable: true),
                    ExternalReferenceText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Hebrew_100_CI_AS"),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
                    EmailThreadId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAssignmentEvent", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_Company",
                        column: x => x.CompanyID,
                        principalTable: "Company",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_Contact",
                        column: x => x.ContactID,
                        principalTable: "Contacts",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_CreatedByUser",
                        column: x => x.CreatedByUserID,
                        principalTable: "SIUser",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_NewStatus",
                        column: x => x.NewStatusID,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_OldStatus",
                        column: x => x.OldStatusID,
                        principalTable: "ProjectAssignmentStatus",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ProjectAssignmentEvent_ProjectAssignment",
                        column: x => x.ProjectAssignmentID,
                        principalTable: "ProjectAssignment",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_StatusID",
                table: "ProjectAssignment",
                column: "StatusID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_TaskTypeID",
                table: "ProjectAssignment",
                column: "TaskTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignment_UniqueTask",
                table: "ProjectAssignment",
                columns: new[] { "ProjectID", "AssignedToID", "TaskTypeID" },
                unique: true,
                filter: "[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_CompanyID",
                table: "ProjectAssignmentEvent",
                column: "CompanyID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_ContactID",
                table: "ProjectAssignmentEvent",
                column: "ContactID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_CreatedByUserID",
                table: "ProjectAssignmentEvent",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_CreatedDate",
                table: "ProjectAssignmentEvent",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_NewStatusID",
                table: "ProjectAssignmentEvent",
                column: "NewStatusID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_OldStatusID",
                table: "ProjectAssignmentEvent",
                column: "OldStatusID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentEvent_ProjectAssignmentID",
                table: "ProjectAssignmentEvent",
                column: "ProjectAssignmentID");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAssignmentStatus_Name",
                table: "ProjectAssignmentStatus",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskType_Name",
                table: "TaskType",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSetting_SIUserID",
                table: "UserSetting",
                column: "SIUserID",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignment_AssignmentStatus",
                table: "ProjectAssignment",
                column: "StatusID",
                principalTable: "ProjectAssignmentStatus",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAssignment_TaskType",
                table: "ProjectAssignment",
                column: "TaskTypeID",
                principalTable: "TaskType",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignment_AssignmentStatus",
                table: "ProjectAssignment");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectAssignment_TaskType",
                table: "ProjectAssignment");

            migrationBuilder.DropTable(
                name: "ProjectAssignmentEvent");

            migrationBuilder.DropTable(
                name: "TaskType");

            migrationBuilder.DropTable(
                name: "UserSetting");

            migrationBuilder.DropTable(
                name: "ProjectAssignmentStatus");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_StatusID",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_TaskTypeID",
                table: "ProjectAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProjectAssignment_UniqueTask",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "StatusID",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "TaskTypeID",
                table: "ProjectAssignment");

            migrationBuilder.DropColumn(
                name: "WorkPriority",
                table: "ProjectAssignment");
        }
    }
}
