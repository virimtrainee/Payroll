using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    public partial class AddBankDetailsToEmployee : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Employees",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IfscCode",
                table: "Employees",
                type: "TEXT",
                maxLength: 15,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IfscCode",
                table: "Employees");
        }
    }
}
