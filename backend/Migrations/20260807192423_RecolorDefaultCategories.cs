using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class RecolorDefaultCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recolore categorias presas na paleta antiga (pastel genérica, nunca validada pra
            // contraste/daltonismo) pra paleta nova, mesma usada em
            // frontend/src/components/charts/chartTheme.js (FALLBACK_COLORS) e
            // backend/Services/PolpSyncService.cs (FallbackPalette). Cobre tanto as 6 cores
            // semeadas no primeiro sync de cada usuário quanto as 7 do fallback determinístico
            // por nome (PickFallbackColor) — os dois conjuntos compartilham 5 tons em comum, daí
            // a tabela abaixo ser a união das duas paletas antigas, cada uma apontando pro slot
            // correspondente da paleta nova. upper() porque nada garante que o hex sempre foi
            // persistido em maiúsculas.
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#3987E5' WHERE upper("Color") IN ('#FF6B6B', '#ED4A31');
                UPDATE "Categories" SET "Color" = '#D95926' WHERE upper("Color") = '#4ECDC4';
                UPDATE "Categories" SET "Color" = '#199E70' WHERE upper("Color") = '#45B7D1';
                UPDATE "Categories" SET "Color" = '#C98500' WHERE upper("Color") = '#96CEB4';
                UPDATE "Categories" SET "Color" = '#D55181' WHERE upper("Color") = '#FFEAA7';
                UPDATE "Categories" SET "Color" = '#008300' WHERE upper("Color") = '#B39DDB';
                UPDATE "Categories" SET "Color" = '#9085E9' WHERE upper("Color") = '#F4A261';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não reversível com fidelidade: '#FF6B6B' (seed) e '#ED4A31' (fallback) colapsam
            // pro mesmo '#3987E5' no Up, então não dá pra saber qual dos dois uma linha era
            // antes. Volta essas pra '#FF6B6B' (a mais comum das duas, por ser a de toda
            // categoria "Alimentação" semeada) em vez de travar o rollback.
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#FF6B6B' WHERE upper("Color") = '#3987E5';
                UPDATE "Categories" SET "Color" = '#4ECDC4' WHERE upper("Color") = '#D95926';
                UPDATE "Categories" SET "Color" = '#45B7D1' WHERE upper("Color") = '#199E70';
                UPDATE "Categories" SET "Color" = '#96CEB4' WHERE upper("Color") = '#C98500';
                UPDATE "Categories" SET "Color" = '#FFEAA7' WHERE upper("Color") = '#D55181';
                UPDATE "Categories" SET "Color" = '#B39DDB' WHERE upper("Color") = '#008300';
                UPDATE "Categories" SET "Color" = '#F4A261' WHERE upper("Color") = '#9085E9';
                """);
        }
    }
}
