using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations;

/// <inheritdoc />
public partial class Init : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Employees",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                BaseSalary = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Employees", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Advances",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                EntryType = table.Column<int>(type: "INTEGER", nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Advances", x => x.Id);
                table.ForeignKey(
                    name: "FK_Advances_Employees_EmployeeId",
                    column: x => x.EmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AttendanceRecords",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                Year = table.Column<int>(type: "INTEGER", nullable: false),
                Month = table.Column<int>(type: "INTEGER", nullable: false),
                DaysAbsent = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                table.ForeignKey(
                    name: "FK_AttendanceRecords_Employees_EmployeeId",
                    column: x => x.EmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Advances_EmployeeId_Date",
            table: "Advances",
            columns: new[] { "EmployeeId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_AttendanceRecords_EmployeeId_Year_Month",
            table: "AttendanceRecords",
            columns: new[] { "EmployeeId", "Year", "Month" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Advances");

        migrationBuilder.DropTable(
            name: "AttendanceRecords");

        migrationBuilder.DropTable(
            name: "Employees");
    }
}
