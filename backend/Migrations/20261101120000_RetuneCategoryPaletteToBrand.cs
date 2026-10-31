using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    // Troca a paleta arco-íris das categorias (azul, laranja, verde, dourado, rosa, verde-escuro,
    // roxo, vermelho) por uma ancorada nas cores do site: laranja-avermelhado #ED4A31 e verde-água.
    // Mapeamento 1:1 por matiz e sem colisão (nenhuma cor nova é igual a uma antiga), então o Down
    // é uma reversão exata. Cor escolhida à mão pelo usuário, fora das 8 antigas, não é tocada.
    // Precisa bater com FALLBACK_COLORS (frontend/src/components/charts/chartTheme.js) e com
    // PolpSyncService.FallbackPalette.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101120000_RetuneCategoryPaletteToBrand")]
    public partial class RetuneCategoryPaletteToBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#6C93C9' WHERE upper("Color") = '#4988D4';
                UPDATE "Categories" SET "Color" = '#ED4A31' WHERE upper("Color") = '#DB5824';
                UPDATE "Categories" SET "Color" = '#3FB8AF' WHERE upper("Color") = '#30A67D';
                UPDATE "Categories" SET "Color" = '#F2A33A' WHERE upper("Color") = '#BF8A22';
                UPDATE "Categories" SET "Color" = '#D9628A' WHERE upper("Color") = '#CD517E';
                UPDATE "Categories" SET "Color" = '#A6BE5C' WHERE upper("Color") = '#328532';
                UPDATE "Categories" SET "Color" = '#9A86D9' WHERE upper("Color") = '#7569D3';
                UPDATE "Categories" SET "Color" = '#B07A5B' WHERE upper("Color") = '#D45454';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#4988D4' WHERE upper("Color") = '#6C93C9';
                UPDATE "Categories" SET "Color" = '#DB5824' WHERE upper("Color") = '#ED4A31';
                UPDATE "Categories" SET "Color" = '#30A67D' WHERE upper("Color") = '#3FB8AF';
                UPDATE "Categories" SET "Color" = '#BF8A22' WHERE upper("Color") = '#F2A33A';
                UPDATE "Categories" SET "Color" = '#CD517E' WHERE upper("Color") = '#D9628A';
                UPDATE "Categories" SET "Color" = '#328532' WHERE upper("Color") = '#A6BE5C';
                UPDATE "Categories" SET "Color" = '#7569D3' WHERE upper("Color") = '#9A86D9';
                UPDATE "Categories" SET "Color" = '#D45454' WHERE upper("Color") = '#B07A5B';
                """);
        }
    }
}
