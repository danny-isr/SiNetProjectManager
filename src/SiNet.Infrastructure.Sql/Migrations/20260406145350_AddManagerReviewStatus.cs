using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerReviewStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "InspectionNoteStatuses",
                columns: new[] { "StatusId", "ExportSymbol", "HebrewLabel", "IsActive", "SortOrder", "StatusKey" },
                values: new object[] { 5, "?", "הערה לבדיקת המנהל", true, 5, "ManagerReview" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "InspectionNoteStatuses",
                keyColumn: "StatusId",
                keyValue: 5);
        }
    }
}
