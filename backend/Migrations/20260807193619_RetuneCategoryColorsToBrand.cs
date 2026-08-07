using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class RetuneCategoryColorsToBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retunagem da RecolorDefaultCategories anterior: mesmos 8 matizes (a separação de
            // matiz é o que garante distinção pra daltonismo, então não muda), só satura/escurece
            // menos "genérico SaaS" e mais perto do clima do site (accent #ED4A31, income
            // #4ECDC4). Mapeamento 1:1 por slot, sem colisão, então o Down é uma reversão exata.
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#4988D4' WHERE upper("Color") = '#3987E5';
                UPDATE "Categories" SET "Color" = '#DB5824' WHERE upper("Color") = '#D95926';
                UPDATE "Categories" SET "Color" = '#30A67D' WHERE upper("Color") = '#199E70';
                UPDATE "Categories" SET "Color" = '#BF8A22' WHERE upper("Color") = '#C98500';
                UPDATE "Categories" SET "Color" = '#CD517E' WHERE upper("Color") = '#D55181';
                UPDATE "Categories" SET "Color" = '#328532' WHERE upper("Color") = '#008300';
                UPDATE "Categories" SET "Color" = '#7569D3' WHERE upper("Color") = '#9085E9';
                UPDATE "Categories" SET "Color" = '#D45454' WHERE upper("Color") = '#E66767';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Categories" SET "Color" = '#3987E5' WHERE upper("Color") = '#4988D4';
                UPDATE "Categories" SET "Color" = '#D95926' WHERE upper("Color") = '#DB5824';
                UPDATE "Categories" SET "Color" = '#199E70' WHERE upper("Color") = '#30A67D';
                UPDATE "Categories" SET "Color" = '#C98500' WHERE upper("Color") = '#BF8A22';
                UPDATE "Categories" SET "Color" = '#D55181' WHERE upper("Color") = '#CD517E';
                UPDATE "Categories" SET "Color" = '#008300' WHERE upper("Color") = '#328532';
                UPDATE "Categories" SET "Color" = '#9085E9' WHERE upper("Color") = '#7569D3';
                UPDATE "Categories" SET "Color" = '#E66767' WHERE upper("Color") = '#D45454';
                """);
        }
    }
}
