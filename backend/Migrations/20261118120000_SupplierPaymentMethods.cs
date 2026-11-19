using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Fornecedor passa a ter vários métodos de pagamento. Pix e conta já cadastrados são copiados
    // como estão (as colunas cifradas usam a mesma chave e o mesmo formato), depois as colunas
    // antigas saem.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261118120000_SupplierPaymentMethods")]
    public partial class SupplierPaymentMethods : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(name: "AK_Suppliers_UserId_Id", table: "Suppliers", columns: new[] { "UserId", "Id" });
            migrationBuilder.CreateTable(name: "SupplierPaymentMethods", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_SupplierPaymentMethods", x => x.Id);
                table.ForeignKey(name: "FK_SupplierPaymentMethod_Supplier", columns: x => new { x.UserId, x.SupplierId }, principalTable: "Suppliers", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.Sql("""
                INSERT INTO "SupplierPaymentMethods" ("Id", "UserId", "SupplierId", "Kind", "PixKeyType", "PixKeyEncrypted", "PixKeyHash", "CreatedAt")
                SELECT gen_random_uuid(), "UserId", "Id", 'Pix', "PixKeyType", "PixKeyEncrypted", "PixKeyHash", "UpdatedAt"
                FROM "Suppliers" WHERE "PixKeyHash" IS NOT NULL AND "PixKeyEncrypted" IS NOT NULL AND "PixKeyType" IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO "SupplierPaymentMethods" ("Id", "UserId", "SupplierId", "Kind", "BankCode", "BranchEncrypted", "AccountEncrypted", "AccountType", "CreatedAt")
                SELECT gen_random_uuid(), "UserId", "Id", 'Bank', "PayeeBankCode", "PayeeBranchEncrypted", "PayeeAccountEncrypted", "PayeeAccountType", "UpdatedAt"
                FROM "Suppliers" WHERE "PayeeBankCode" IS NOT NULL AND "PayeeBranchEncrypted" IS NOT NULL
                    AND "PayeeAccountEncrypted" IS NOT NULL AND "PayeeAccountType" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMethods_SupplierId_CreatedAt", table: "SupplierPaymentMethods", columns: new[] { "SupplierId", "CreatedAt" });
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMethods_UserId_SupplierId", table: "SupplierPaymentMethods", columns: new[] { "UserId", "SupplierId" });
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMethods_UserId_PixKeyHash", table: "SupplierPaymentMethods", columns: new[] { "UserId", "PixKeyHash" }, unique: true, filter: "\"PixKeyHash\" IS NOT NULL");

            migrationBuilder.DropIndex(name: "IX_Suppliers_UserId_PixKeyHash", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PixKeyType", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PixKeyEncrypted", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PixKeyHash", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PayeeBankCode", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PayeeBranchEncrypted", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PayeeAccountEncrypted", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PayeeAccountType", table: "Suppliers");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "PixKeyType", table: "Suppliers", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PixKeyEncrypted", table: "Suppliers", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PixKeyHash", table: "Suppliers", type: "character varying(120)", maxLength: 120, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeBankCode", table: "Suppliers", type: "character varying(3)", maxLength: 3, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeBranchEncrypted", table: "Suppliers", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeAccountEncrypted", table: "Suppliers", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PayeeAccountType", table: "Suppliers", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Suppliers_UserId_PixKeyHash", table: "Suppliers", columns: new[] { "UserId", "PixKeyHash" }, unique: true, filter: "\"PixKeyHash\" IS NOT NULL");
            migrationBuilder.DropTable(name: "SupplierPaymentMethods");
            migrationBuilder.DropUniqueConstraint(name: "AK_Suppliers_UserId_Id", table: "Suppliers");
        }
    }
}
