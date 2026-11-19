using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Titular (CPF/CNPJ) de quem recebe em cada método de pagamento, cifrado; métodos antigos ficam sem
    // titular (nada é presumido). Transactions ganha a origem do CNPJ da contraparte: começa falso e o
    // sync (TransactionDetailsVersion 5) relê o histórico para marcar o que veio do banco.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119180000_PaymentMethodHolderDocument")]
    public partial class PaymentMethodHolderDocument : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "EmployeePaymentMethods", "SupplierPaymentMethods" })
            {
                migrationBuilder.AddColumn<string>(name: "HolderDocumentType", table: table, type: "character varying(4)", maxLength: 4, nullable: true);
                migrationBuilder.AddColumn<string>(name: "HolderDocumentEncrypted", table: table, type: "text", nullable: true);
            }
            migrationBuilder.AddColumn<bool>(name: "CounterpartyCnpjFromBank", table: "Transactions", type: "boolean", nullable: false, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "EmployeePaymentMethods", "SupplierPaymentMethods" })
            {
                migrationBuilder.DropColumn(name: "HolderDocumentType", table: table);
                migrationBuilder.DropColumn(name: "HolderDocumentEncrypted", table: table);
            }
            migrationBuilder.DropColumn(name: "CounterpartyCnpjFromBank", table: "Transactions");
        }
    }
}
