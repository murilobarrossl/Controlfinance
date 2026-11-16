using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullNameEncrypted = table.Column<string>(type: "text", nullable: false),
                    CpfEncrypted = table.Column<string>(type: "text", nullable: false),
                    CpfHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AdmissionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EmploymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GrossSalaryEncrypted = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TerminationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PixKeyType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PixKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                    PixKeyHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PayeeBankCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PayeeBranchEncrypted = table.Column<string>(type: "text", nullable: true),
                    PayeeAccountEncrypted = table.Column<string>(type: "text", nullable: true),
                    PayeeAccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserId_CpfHash",
                table: "Employees",
                columns: new[] { "UserId", "CpfHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserId_PixKeyHash",
                table: "Employees",
                columns: new[] { "UserId", "PixKeyHash" },
                unique: true,
                filter: "\"PixKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserId_Status_Department",
                table: "Employees",
                columns: new[] { "UserId", "Status", "Department" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
