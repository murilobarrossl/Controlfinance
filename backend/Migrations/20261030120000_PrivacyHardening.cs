using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    // Escrita à mão (sem `dotnet ef migrations add`, que compila o projeto): por isso os atributos
    // ficam aqui em vez de num .Designer.cs. Migrate() só precisa deles pra descobrir e aplicar a
    // migration; o AppDbContextModelSnapshot foi atualizado à mão pra refletir o mesmo modelo.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261030120000_PrivacyHardening")]
    public partial class PrivacyHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Telefone era obrigatório no cadastro e não tinha nenhum uso no sistema.
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");

            // CPF/CNPJ recuperável deixa de ser obrigatório: PrivacyBackfill grava DocumentType e
            // zera esse campo nas linhas existentes. A coluna é removida numa migration posterior,
            // só depois de o backfill ter rodado (mesmo cuidado de EncryptSensitiveData →
            // DropLegacyPlaintextColumns: nunca derrubar a coluna antes do dado ter sido tratado).
            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "Users",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DueRemindersEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Resumo mensal passa a ser opt-in: todo mundo começa desligado.
            migrationBuilder.AddColumn<bool>(
                name: "MonthlySummaryEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Nome da transação passa a ser cifrado em NameEncrypted. A coluna antiga "Name"
            // (texto puro) vira nulável e é esvaziada pelo PrivacyBackfill linha a linha.
            migrationBuilder.AddColumn<string>(
                name: "NameEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Transactions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            // URL de autenticação é de uso único e só serve no momento da conexão: o endpoint
            // de status já devolve a versão atual direto da Polp, guardar não tinha função.
            migrationBuilder.DropColumn(
                name: "UrlToAuthenticate",
                table: "PolpIntegrations");

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_UserId_Kind",
                table: "ConsentRecords",
                columns: new[] { "UserId", "Kind" });

            // Sem FK pra Users de propósito: precisa sobreviver à exclusão da conta.
            migrationBuilder.CreateTable(
                name: "PendingProviderDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolpIntegrationId = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingProviderDeletions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversão só de esquema: telefone, URL de autenticação e CPF/CNPJ zerado não voltam
            // (o dado não existe mais em lugar nenhum pra ser restaurado).
            migrationBuilder.DropTable(
                name: "PendingProviderDeletions");

            migrationBuilder.DropTable(
                name: "ConsentRecords");

            migrationBuilder.AddColumn<string>(
                name: "UrlToAuthenticate",
                table: "PolpIntegrations",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Transactions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "NameEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "MonthlySummaryEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DueRemindersEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
