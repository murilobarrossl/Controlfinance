using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119130000_PayeePaymentMarkEvents")]
    public partial class PayeePaymentMarkEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "Version", table: "EmployeePaymentMarks", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Version", table: "SupplierPaymentMarks", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.CreateTable(name: "PayeePaymentMarkEvents", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                PayeeKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                PayeeId = table.Column<Guid>(type: "uuid", nullable: false),
                ReferenceMonth = table.Column<DateOnly>(type: "date", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankIdentified = table.Column<bool>(type: "boolean", nullable: false),
                BankPaidAmountEncrypted = table.Column<string>(type: "text", nullable: true),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_PayeePaymentMarkEvents", x => x.Id);
                table.ForeignKey(name: "FK_PayeePaymentMarkEvents_Users_UserId", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.CreateIndex(name: "IX_PayeePaymentMarkEvents_UserId_PayeeKind_PayeeId_ReferenceMonth_Version", table: "PayeePaymentMarkEvents",
                columns: new[] { "UserId", "PayeeKind", "PayeeId", "ReferenceMonth", "Version" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PayeePaymentMarkEvents");
            migrationBuilder.DropColumn(name: "Version", table: "EmployeePaymentMarks");
            migrationBuilder.DropColumn(name: "Version", table: "SupplierPaymentMarks");
        }
    }
}
