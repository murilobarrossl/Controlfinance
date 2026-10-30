using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    // Escrita à mão, como PrivacyHardening (ver comentário lá). Só ADICIONA estrutura e afrouxa
    // um NOT NULL: nenhum dado é apagado ou transformado aqui. Down() é seguro enquanto o
    // PrivacyBackfill não tiver cifrado as descrições; depois disso, voltar exige decifrar antes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261031120000_InstallmentEncryptionAndAccessLog")]
    public partial class InstallmentEncryptionAndAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionEncrypted",
                table: "Installments",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Installments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            // Registro de acesso (Marco Civil), vazio e sem uso enquanto AccessLog:Mode = Off.
            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Event = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IpAddressEncrypted = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_OccurredAt",
                table: "AccessLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Installments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "DescriptionEncrypted",
                table: "Installments");
        }
    }
}
