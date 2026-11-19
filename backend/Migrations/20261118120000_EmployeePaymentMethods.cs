using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Funcionário passa a ter vários métodos de pagamento. Pix e conta já cadastrados são copiados
    // como estão (as colunas cifradas usam a mesma chave e o mesmo formato), depois as colunas
    // antigas saem.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261118120000_EmployeePaymentMethods")]
    public partial class EmployeePaymentMethods : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(name: "AK_Employees_UserId_Id", table: "Employees", columns: new[] { "UserId", "Id" });
            migrationBuilder.CreateTable(name: "EmployeePaymentMethods", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                PixKeyType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                PixKeyEncrypted = table.Column<string>(type: "text", nullable: true),
                PixKeyHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                BankCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                BranchEncrypted = table.Column<string>(type: "text", nullable: true),
                AccountEncrypted = table.Column<string>(type: "text", nullable: true),
                AccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_EmployeePaymentMethods", x => x.Id);
                table.ForeignKey(name: "FK_EmployeePaymentMethod_Employee", columns: x => new { x.UserId, x.EmployeeId }, principalTable: "Employees", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.Sql("""
                INSERT INTO "EmployeePaymentMethods" ("Id", "UserId", "EmployeeId", "Kind", "PixKeyType", "PixKeyEncrypted", "PixKeyHash", "CreatedAt")
                SELECT gen_random_uuid(), "UserId", "Id", 'Pix', "PixKeyType", "PixKeyEncrypted", "PixKeyHash", "UpdatedAt"
                FROM "Employees" WHERE "PixKeyHash" IS NOT NULL AND "PixKeyEncrypted" IS NOT NULL AND "PixKeyType" IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO "EmployeePaymentMethods" ("Id", "UserId", "EmployeeId", "Kind", "BankCode", "BranchEncrypted", "AccountEncrypted", "AccountType", "CreatedAt")
                SELECT gen_random_uuid(), "UserId", "Id", 'Bank', "PayeeBankCode", "PayeeBranchEncrypted", "PayeeAccountEncrypted", "PayeeAccountType", "UpdatedAt"
                FROM "Employees" WHERE "PayeeBankCode" IS NOT NULL AND "PayeeBranchEncrypted" IS NOT NULL
                    AND "PayeeAccountEncrypted" IS NOT NULL AND "PayeeAccountType" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(name: "IX_EmployeePaymentMethods_EmployeeId_CreatedAt", table: "EmployeePaymentMethods", columns: new[] { "EmployeeId", "CreatedAt" });
            migrationBuilder.CreateIndex(name: "IX_EmployeePaymentMethods_UserId_EmployeeId", table: "EmployeePaymentMethods", columns: new[] { "UserId", "EmployeeId" });
            migrationBuilder.CreateIndex(name: "IX_EmployeePaymentMethods_UserId_PixKeyHash", table: "EmployeePaymentMethods", columns: new[] { "UserId", "PixKeyHash" }, unique: true, filter: "\"PixKeyHash\" IS NOT NULL");

            migrationBuilder.DropIndex(name: "IX_Employees_UserId_PixKeyHash", table: "Employees");
            migrationBuilder.DropColumn(name: "PixKeyType", table: "Employees");
            migrationBuilder.DropColumn(name: "PixKeyEncrypted", table: "Employees");
            migrationBuilder.DropColumn(name: "PixKeyHash", table: "Employees");
            migrationBuilder.DropColumn(name: "PayeeBankCode", table: "Employees");
            migrationBuilder.DropColumn(name: "PayeeBranchEncrypted", table: "Employees");
            migrationBuilder.DropColumn(name: "PayeeAccountEncrypted", table: "Employees");
            migrationBuilder.DropColumn(name: "PayeeAccountType", table: "Employees");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "PixKeyType", table: "Employees", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PixKeyEncrypted", table: "Employees", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PixKeyHash", table: "Employees", type: "character varying(120)", maxLength: 120, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeBankCode", table: "Employees", type: "character varying(3)", maxLength: 3, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeBranchEncrypted", table: "Employees", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeAccountEncrypted", table: "Employees", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeAccountType", table: "Employees", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Employees_UserId_PixKeyHash", table: "Employees", columns: new[] { "UserId", "PixKeyHash" }, unique: true, filter: "\"PixKeyHash\" IS NOT NULL");
            migrationBuilder.DropTable(name: "EmployeePaymentMethods");
            migrationBuilder.DropUniqueConstraint(name: "AK_Employees_UserId_Id", table: "Employees");
        }
    }
}
