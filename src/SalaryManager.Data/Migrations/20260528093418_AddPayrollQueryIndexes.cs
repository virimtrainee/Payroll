using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SalaryRevisions_ChangedAt",
                table: "SalaryRevisions",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_Year_Month_EmployeeId",
                table: "AttendanceRecords",
                columns: new[] { "Year", "Month", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Advances_Date_Id",
                table: "Advances",
                columns: new[] { "Date", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Advances_SourceKey_EntryType_EmployeeId",
                table: "Advances",
                columns: new[] { "SourceKey", "EntryType", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalaryRevisions_ChangedAt",
                table: "SalaryRevisions");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_Year_Month_EmployeeId",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_Advances_Date_Id",
                table: "Advances");

            migrationBuilder.DropIndex(
                name: "IX_Advances_SourceKey_EntryType_EmployeeId",
                table: "Advances");
        }
    }
}
