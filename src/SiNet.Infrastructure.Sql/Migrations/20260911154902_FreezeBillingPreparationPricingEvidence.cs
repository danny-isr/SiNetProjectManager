using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class FreezeBillingPreparationPricingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricingBaseAmount",
                table: "BillingPreparationStageLine",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingCalculatedAmount",
                table: "BillingPreparationStageLine",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingDiscountFraction",
                table: "BillingPreparationStageLine",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingUnavailableReason",
                table: "BillingPreparationStageLine",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingFormulaVersion",
                table: "BillingPreparationRequest",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PricingFrozenAtUtc",
                table: "BillingPreparationRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingHoursTotal",
                table: "BillingPreparationRequest",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricingIsPartial",
                table: "BillingPreparationRequest",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PricingSourceSnapshotUtc",
                table: "BillingPreparationRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingStageTotal",
                table: "BillingPreparationRequest",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingTotal",
                table: "BillingPreparationRequest",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingCalculatedAmount",
                table: "BillingPreparationHoursLine",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingDiscountFraction",
                table: "BillingPreparationHoursLine",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingHourlyRate",
                table: "BillingPreparationHoursLine",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingUnavailableReason",
                table: "BillingPreparationHoursLine",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricingBaseAmount",
                table: "BillingPreparationStageLine");

            migrationBuilder.DropColumn(
                name: "PricingCalculatedAmount",
                table: "BillingPreparationStageLine");

            migrationBuilder.DropColumn(
                name: "PricingDiscountFraction",
                table: "BillingPreparationStageLine");

            migrationBuilder.DropColumn(
                name: "PricingUnavailableReason",
                table: "BillingPreparationStageLine");

            migrationBuilder.DropColumn(
                name: "PricingFormulaVersion",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingFrozenAtUtc",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingHoursTotal",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingIsPartial",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingSourceSnapshotUtc",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingStageTotal",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingTotal",
                table: "BillingPreparationRequest");

            migrationBuilder.DropColumn(
                name: "PricingCalculatedAmount",
                table: "BillingPreparationHoursLine");

            migrationBuilder.DropColumn(
                name: "PricingDiscountFraction",
                table: "BillingPreparationHoursLine");

            migrationBuilder.DropColumn(
                name: "PricingHourlyRate",
                table: "BillingPreparationHoursLine");

            migrationBuilder.DropColumn(
                name: "PricingUnavailableReason",
                table: "BillingPreparationHoursLine");
        }
    }
}
