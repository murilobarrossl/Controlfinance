using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalNameEncrypted = table.Column<string>(type: "text", nullable: false),
                    TradeNameEncrypted = table.Column<string>(type: "text", nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    DocumentEncrypted = table.Column<string>(type: "text", nullable: false),
                    DocumentHash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UserId_DocumentHash",
                table: "Suppliers",
                columns: new[] { "UserId", "DocumentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UserId_PixKeyHash",
                table: "Suppliers",
                columns: new[] { "UserId", "PixKeyHash" },
                unique: true,
                filter: "\"PixKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UserId_Status",
                table: "Suppliers",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
