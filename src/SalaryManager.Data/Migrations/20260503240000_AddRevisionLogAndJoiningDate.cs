using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    [Migration("20260503240000_AddRevisionLogAndJoiningDate")]
    [DbContext(typeof(SalaryManager.Data.AppDbContext))]
    public partial class AddRevisionLogAndJoiningDate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "JoiningDate",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalaryRevisions",
                columns: t => new
                {
                    Id         = t.Column<int>(type: "INTEGER", nullable: false)
                                  .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = t.Column<int>(type: "INTEGER", nullable: false),
                    OldSalary  = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    NewSalary  = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ChangedAt  = t.Column<DateTime>(type: "TEXT", nullable: false),
                    Note       = t.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_SalaryRevisions", x => x.Id);
                    t.ForeignKey(
                        name: "FK_SalaryRevisions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRevisions_EmployeeId_ChangedAt",
                table: "SalaryRevisions",
                columns: new[] { "EmployeeId", "ChangedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SalaryRevisions");

            migrationBuilder.DropColumn(
                name: "JoiningDate",
                table: "Employees");
        }
    }
}
