using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingReviewDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingReviewDecision",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectID = table.Column<int>(type: "int", nullable: false),
                    DecisionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewAgainDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByLogin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedByLogin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClearedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClearedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClearedByLogin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ObservedLatestBillId = table.Column<int>(type: "int", nullable: true),
                    ObservedLatestBillStatusId = table.Column<int>(type: "int", nullable: true),
                    ObservedLastBillDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObservedHoursSinceLastBill = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    ObservedLastWorkDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingReviewDecision", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_BillingReviewDecision_ProjectID",
                table: "BillingReviewDecision",
                column: "ProjectID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingReviewDecision");
        }
    }
}
