using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    public partial class ReplaceIsIciciBankWithPaymentMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentMode",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2); // OtherBank

            // Migrate existing ICICI-bank employees (IsIciciBank = 1 → PaymentMode = 1)
            migrationBuilder.Sql("UPDATE Employees SET PaymentMode = 1 WHERE IsIciciBank = 1");

            migrationBuilder.DropColumn(
                name: "IsIciciBank",
                table: "Employees");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIciciBank",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE Employees SET IsIciciBank = 1 WHERE PaymentMode = 1");

            migrationBuilder.DropColumn(
                name: "PaymentMode",
                table: "Employees");
        }
    }
}
