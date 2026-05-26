using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalaryManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsTdsAdvanceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "Advances",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TdsDeduction",
                table: "AttendanceRecords",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "EmployeeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeGroupMemberships",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeGroupId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeGroupMemberships", x => new { x.EmployeeId, x.EmployeeGroupId });
                    table.ForeignKey(
                        name: "FK_EmployeeGroupMemberships_EmployeeGroups_EmployeeGroupId",
                        column: x => x.EmployeeGroupId,
                        principalTable: "EmployeeGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeGroupMemberships_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                UPDATE Advances
                SET SourceKey = Note
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT
                            Id,
                            ROW_NUMBER() OVER (
                                PARTITION BY EmployeeId, Note
                                ORDER BY Date DESC, Id DESC
                            ) AS RowNumber
                        FROM Advances
                        WHERE Note GLOB 'SAL:[0-9][0-9][0-9][0-9]-[0-9][0-9]'
                            AND EntryType = 1
                            AND CAST(substr(Note, 10, 2) AS INTEGER) BETWEEN 1 AND 12
                    )
                    WHERE RowNumber = 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Advances_EmployeeId_SourceKey",
                table: "Advances",
                columns: new[] { "EmployeeId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeGroupMemberships_EmployeeGroupId_EmployeeId",
                table: "EmployeeGroupMemberships",
                columns: new[] { "EmployeeGroupId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeGroups_NormalizedName",
                table: "EmployeeGroups",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeGroupMemberships");

            migrationBuilder.DropTable(
                name: "EmployeeGroups");

            migrationBuilder.DropIndex(
                name: "IX_Advances_EmployeeId_SourceKey",
                table: "Advances");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "Advances");

            migrationBuilder.DropColumn(
                name: "TdsDeduction",
                table: "AttendanceRecords");
        }
    }
}
