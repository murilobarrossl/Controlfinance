using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCategoryNamePerUser : Migration
    {
        // ══════════════════════════════════════════════════════════════════════════════════
        // NÃO FAZ DEPLOY DESTA MIGRATION EM PRODUÇÃO ANTES DE RODAR A LIMPEZA DA FASE C.
        // Em 2026-07-28 existiam 4 pares de categoria duplicada (mesmo UserId+Name) no banco de
        // produção. O CREATE UNIQUE INDEX abaixo falha se qualquer duplicata ainda existir
        // (Postgres não cria índice único sobre dado que já o viola). A guarda SQL logo no início
        // do Up() torna esse erro explícito ("rode a Fase C antes") em vez do erro genérico do
        // Postgres sobre índice único, mas a ordem certa continua sendo: 1) Fase C (DELETE dos
        // duplicados) primeiro, 2) esta migration depois.
        // ══════════════════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Categories"
                        GROUP BY "UserId", "Name"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'AddUniqueCategoryNamePerUser abortada: ainda existem categorias duplicadas (mesmo UserId + Name). Rode a limpeza da Fase C antes de aplicar esta migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_Name",
                table: "Categories",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId_Name",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");
        }
    }
}
