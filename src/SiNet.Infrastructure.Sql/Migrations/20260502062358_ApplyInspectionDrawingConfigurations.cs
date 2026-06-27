using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class ApplyInspectionDrawingConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReportDrawings_InspectionReports_ReportId",
                table: "InspectionReportDrawings");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReportDrawings_ProjectFileInstance_FileInstanceId",
                table: "InspectionReportDrawings");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionSeriesFileConfigs_InspectionSeries_SeriesId",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionSeriesFileConfigs_ProjectFile_ProjectFileId",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.DropIndex(
                name: "IX_InspectionSeriesFileConfigs_SeriesId",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "InspectionSeriesFileConfigs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "StampedFilePath",
                table: "InspectionReportDrawings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StampStatus",
                table: "InspectionReportDrawings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStamped",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "SourceFilePath",
                table: "InspectionReportDrawings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SelectedLayoutIndices",
                table: "InspectionReportDrawings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FileType",
                table: "InspectionReportDrawings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "InspectionReportDrawings",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesFileConfigs_Series_File_Role",
                table: "InspectionSeriesFileConfigs",
                columns: new[] { "SeriesId", "ProjectFileId", "Role" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReportDrawing_FileInstance",
                table: "InspectionReportDrawings",
                column: "FileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReportDrawing_Report",
                table: "InspectionReportDrawings",
                column: "ReportId",
                principalTable: "InspectionReports",
                principalColumn: "ReportId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SeriesFileConfig_ProjectFile",
                table: "InspectionSeriesFileConfigs",
                column: "ProjectFileId",
                principalTable: "ProjectFile",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SeriesFileConfig_Series",
                table: "InspectionSeriesFileConfigs",
                column: "SeriesId",
                principalTable: "InspectionSeries",
                principalColumn: "SeriesId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReportDrawing_FileInstance",
                table: "InspectionReportDrawings");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionReportDrawing_Report",
                table: "InspectionReportDrawings");

            migrationBuilder.DropForeignKey(
                name: "FK_SeriesFileConfig_ProjectFile",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_SeriesFileConfig_Series",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.DropIndex(
                name: "IX_SeriesFileConfigs_Series_File_Role",
                table: "InspectionSeriesFileConfigs");

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "InspectionSeriesFileConfigs",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "StampedFilePath",
                table: "InspectionReportDrawings",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "StampStatus",
                table: "InspectionReportDrawings",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "NotStamped");

            migrationBuilder.AlterColumn<string>(
                name: "SourceFilePath",
                table: "InspectionReportDrawings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "SelectedLayoutIndices",
                table: "InspectionReportDrawings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldDefaultValue: "[]");

            migrationBuilder.AlterColumn<int>(
                name: "FileType",
                table: "InspectionReportDrawings",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "InspectionReportDrawings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(260)",
                oldMaxLength: 260);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionSeriesFileConfigs_SeriesId",
                table: "InspectionSeriesFileConfigs",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReportDrawings_InspectionReports_ReportId",
                table: "InspectionReportDrawings",
                column: "ReportId",
                principalTable: "InspectionReports",
                principalColumn: "ReportId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionReportDrawings_ProjectFileInstance_FileInstanceId",
                table: "InspectionReportDrawings",
                column: "FileInstanceId",
                principalTable: "ProjectFileInstance",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionSeriesFileConfigs_InspectionSeries_SeriesId",
                table: "InspectionSeriesFileConfigs",
                column: "SeriesId",
                principalTable: "InspectionSeries",
                principalColumn: "SeriesId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionSeriesFileConfigs_ProjectFile_ProjectFileId",
                table: "InspectionSeriesFileConfigs",
                column: "ProjectFileId",
                principalTable: "ProjectFile",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
