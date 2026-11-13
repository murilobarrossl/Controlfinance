using ControlFinance.API.Services;

namespace ControlFinance.API.Tests;

public class BankBrandColorsTests
{
    [Theory]
    [InlineData("Banco XP S.A.", "#000000")]
    [InlineData("C6 Bank", "#242424")]
    [InlineData("Itaú", "#EC7000")]
    public void Instituicao_conhecida_tem_cor(string name, string color) => Assert.Equal(color, BankBrandColors.Find(name));

    [Fact]
    public void Nome_que_so_contem_xp_dentro_de_outra_palavra_nao_casa() => Assert.Null(BankBrandColors.Find("Banco Expresso"));
}
