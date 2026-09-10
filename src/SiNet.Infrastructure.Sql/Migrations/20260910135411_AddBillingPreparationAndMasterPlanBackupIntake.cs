using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPreparationAndMasterPlanBackupIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingPreparationRequest",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MasterPlanProjectID = table.Column<int>(type: "int", nullable: false),
                    SiNetProjectID = table.Column<int>(type: "int", nullable: true),
                    ProjectNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProjectName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByLogin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedByLogin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SnapshotTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LatestMasterPlanBackupUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TaskId = table.Column<int>(type: "int", nullable: true),
                    ManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    ManualOverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManualOverrideAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManualOverrideByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPreparationRequest", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "MasterPlanBackupIntake",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    GmailMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IncomingPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BackupFinishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterPlanBackupIntake", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "BillingPreparationHoursLine",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestID = table.Column<int>(type: "int", nullable: false),
                    MasterPlanSubContractID = table.Column<int>(type: "int", nullable: false),
                    SubContractName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportCount = table.Column<int>(type: "int", nullable: false),
                    TotalHours = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    OverlappingHourReportIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SnapshotTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmationMode = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPreparationHoursLine", x => x.ID);
                    table.ForeignKey(
                        name: "FK_BillingPreparationHoursLine_BillingPreparationRequest_RequestID",
                        column: x => x.RequestID,
                        principalTable: "BillingPreparationRequest",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingPreparationStageLine",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestID = table.Column<int>(type: "int", nullable: false),
                    MasterPlanStageID = table.Column<int>(type: "int", nullable: false),
                    MasterPlanSubContractID = table.Column<int>(type: "int", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubContractName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StageWeightWithinSubContract = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ObservedCumulativeProgress = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    TargetCumulativeProgress = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    RequestedDelta = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    HasDataQualityFlag = table.Column<bool>(type: "bit", nullable: false),
                    SnapshotTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmationMode = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPreparationStageLine", x => x.ID);
                    table.ForeignKey(
                        name: "FK_BillingPreparationStageLine_BillingPreparationRequest_RequestID",
                        column: x => x.RequestID,
                        principalTable: "BillingPreparationRequest",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingPreparationHoursReport",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HoursLineID = table.Column<int>(type: "int", nullable: false),
                    HoursReportID = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeID = table.Column<int>(type: "int", nullable: true),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Hours = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SubContractID = table.Column<int>(type: "int", nullable: true),
                    SubContractStepID = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPreparationHoursReport", x => x.ID);
                    table.ForeignKey(
                        name: "FK_BillingPreparationHoursReport_BillingPreparationHoursLine_HoursLineID",
                        column: x => x.HoursLineID,
                        principalTable: "BillingPreparationHoursLine",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPreparationHoursLine_RequestID",
                table: "BillingPreparationHoursLine",
                column: "RequestID");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPreparationHoursReport_HoursReportID",
                table: "BillingPreparationHoursReport",
                column: "HoursReportID");

            migrationBuilder.CreateIndex(
                name: "UX_BillingPreparationHoursReport_Line_Report",
                table: "BillingPreparationHoursReport",
                columns: new[] { "HoursLineID", "HoursReportID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPreparationRequest_MasterPlanProjectID",
                table: "BillingPreparationRequest",
                column: "MasterPlanProjectID");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPreparationRequest_Status",
                table: "BillingPreparationRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPreparationRequest_TaskID",
                table: "BillingPreparationRequest",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "UX_BillingPreparationStageLine_Request_Stage",
                table: "BillingPreparationStageLine",
                columns: new[] { "RequestID", "MasterPlanStageID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterPlanBackupIntake_Status",
                table: "MasterPlanBackupIntake",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_MasterPlanBackupIntake_Sha256",
                table: "MasterPlanBackupIntake",
                column: "Sha256",
                unique: true,
                filter: "[Sha256] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingPreparationHoursReport");

            migrationBuilder.DropTable(
                name: "BillingPreparationStageLine");

            migrationBuilder.DropTable(
                name: "MasterPlanBackupIntake");

            migrationBuilder.DropTable(
                name: "BillingPreparationHoursLine");

            migrationBuilder.DropTable(
                name: "BillingPreparationRequest");
        }
    }
}
