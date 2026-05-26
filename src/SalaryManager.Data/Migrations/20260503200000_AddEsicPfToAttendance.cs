using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations;

public partial class AddEsicPfToAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "EsicDeduction",
            table: "AttendanceRecords",
            type: "DECIMAL(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "PfDeduction",
            table: "AttendanceRecords",
            type: "DECIMAL(18,2)",
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EsicDeduction",
            table: "AttendanceRecords");

        migrationBuilder.DropColumn(
            name: "PfDeduction",
            table: "AttendanceRecords");
    }
}
