using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class CreditCardSummaryTests
{
    private static Task<List<RecognizedCardDto>> Build(TestDb t) =>
        CreditCardSummaryBuilder.BuildRecognizedListAsync(t.Db, t.User.Id, Dates.Today);

    [Fact]
    public async Task Cartao_sincronizado_repassa_limite_usado_fatura_e_vencimento_gravados()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Banco", type: "CREDIT_CARD", creditLimit: 8000.00m, usedLimit: 2345.67m);
        account.CurrentInvoiceAmount = 987.65m;
        account.InvoiceDueDate = new DateTime(2099, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        account.ClosingDay = 3;
        account.DueDay = 10;
        await t.Db.SaveChangesAsync();

        var dto = Assert.Single(await Build(t));

        Assert.True(dto.IsSynced);
        Assert.Equal(8000.00m, dto.CreditLimit);
        Assert.Equal(2345.67m, dto.UsedLimit);
        Assert.Equal(987.65m, dto.CurrentInvoice);
        Assert.Equal(new DateTime(2099, 3, 10), dto.InvoiceDueDate);
        Assert.True(dto.HasFullDetails);
    }

    [Fact]
    public async Task Cartao_sincronizado_sem_limite_nao_tem_detalhes_completos_e_nao_inventa_numeros()
    {
        using var t = new TestDb();
        t.AddAccount("Cartao Banco", type: "CREDIT_CARD");

        var dto = Assert.Single(await Build(t));

        Assert.False(dto.HasFullDetails);
        Assert.Null(dto.CreditLimit);
        Assert.Null(dto.UsedLimit);
        Assert.Null(dto.CurrentInvoice);
    }

    [Fact]
    public async Task Conta_corrente_nao_aparece_como_cartao()
    {
        using var t = new TestDb();
        t.AddAccount("Corrente", type: "BANK");

        Assert.Empty(await Build(t));
    }

    [Fact]
    public async Task Cartao_inativo_nao_entra_no_resumo()
    {
        using var t = new TestDb();
        t.AddAccount("Cartao", active: false, type: "CREDIT_CARD");

        Assert.Empty(await Build(t));
    }

    [Fact]
    public async Task Endpoint_de_resumo_devolve_os_mesmos_numeros_do_builder()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 700m);
        account.CurrentInvoiceAmount = 100m;
        await t.Db.SaveChangesAsync();

        var result = await t.Controller(db => new CreditCardsController(db)).GetSummary(default);

        var dto = Assert.Single(Results.OkValue<List<RecognizedCardDto>>(result));
        Assert.Equal(700.00m, dto.UsedLimit);
        Assert.Equal(100.00m, dto.CurrentInvoice);
    }
}
