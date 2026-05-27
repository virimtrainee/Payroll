using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNetSalaryOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NetSalaryOverride",
                table: "AttendanceRecords",
                type: "DECIMAL(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NetSalaryOverride",
                table: "AttendanceRecords");
        }
    }
}
