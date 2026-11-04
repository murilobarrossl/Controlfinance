using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

public class InstallmentsControllerTests
{
    private static InstallmentsController Controller(TestDb t) => t.Controller(db => new InstallmentsController(db));

    private static CreateInstallmentDto Dto(Guid? cardId, decimal total, int totalInstallments, int current,
        decimal amount, Guid? accountId = null) =>
        new(cardId, accountId, "Notebook", total, totalInstallments, current, amount, new DateTime(2099, 1, 10));

    [Fact]
    public async Task Criar_12x100_na_1a_parcela_soma_1200_ao_usado_e_disponivel_vira_3800()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);

        var result = await Controller(t).Create(Dto(card.Id, 1200m, 12, 1, 100m));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1200.00m, card.UsedLimit);
        Assert.Equal(3800.00m, CreditCardDto.FromEntity(card).AvailableLimit);
    }

    [Fact]
    public async Task Criar_ja_na_6a_de_12_compromete_so_7_parcelas_restantes_700()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);

        await Controller(t).Create(Dto(card.Id, 1200m, 12, 6, 100m));

        Assert.Equal(700.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_unica_compromete_o_valor_inteiro()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);

        await Controller(t).Create(Dto(card.Id, 250.50m, 1, 1, 250.50m));

        Assert.Equal(250.50m, card.UsedLimit);
    }

    [Fact]
    public async Task Valor_da_parcela_e_gravado_exatamente_como_informado_inclusive_centavos()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);

        await Controller(t).Create(Dto(card.Id, 100.00m, 3, 1, 33.33m));

        await using var other = t.NewContext();
        var saved = await other.Installments.SingleAsync();
        Assert.Equal(33.33m, saved.InstallmentAmount);
        Assert.Equal(100.00m, saved.TotalAmount);
        Assert.Equal(99.99m, (await other.CreditCards.SingleAsync()).UsedLimit);
    }

    [Fact]
    public async Task Criar_100_em_3x_de_33_33_e_excluir_devolve_o_usado_a_zero_sem_residuo_de_centavo()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);
        var controller = Controller(t);
        await controller.Create(Dto(card.Id, 100.00m, 3, 1, 33.33m));
        var id = (await t.Db.Installments.SingleAsync()).Id;

        var result = await controller.Delete(id);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Criar_que_leva_o_usado_exatamente_ao_limite_e_aceito()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 3800m);

        var result = await Controller(t).Create(Dto(card.Id, 1200m, 12, 1, 100m));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(5000.00m, card.UsedLimit);
        Assert.Equal(0.00m, CreditCardDto.FromEntity(card).AvailableLimit);
    }

    [Fact]
    public async Task Criar_que_ultrapassa_o_limite_por_1_centavo_e_recusado_e_nao_altera_o_usado()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 3800.01m);

        var result = await Controller(t).Create(Dto(card.Id, 1200m, 12, 1, 100m));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(3800.01m, card.UsedLimit);
        Assert.Empty(await t.Db.Installments.ToListAsync());
    }

    [Fact]
    public async Task Parcela_atual_maior_que_total_e_recusada()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);

        var result = await Controller(t).Create(Dto(card.Id, 100m, 3, 4, 33.33m));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Cartao_manual_e_conta_ao_mesmo_tempo_e_recusado()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);
        var account = t.AddAccount("Cartao", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 0m);

        var result = await Controller(t).Create(Dto(card.Id, 100m, 1, 1, 100m, account.Id));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cartao_de_outro_usuario_e_recusado_e_nao_e_alterado()
    {
        using var t = new TestDb();
        var other = new ControlFinance.API.Models.User { Name = "Outro", Email = "o@x.com", DocumentHash = "h2", PasswordHash = "x" };
        t.Db.Users.Add(other);
        var foreign = new ControlFinance.API.Models.CreditCard { UserId = other.Id, Name = "Alheio", CreditLimit = 5000m, UsedLimit = 0m };
        t.Db.CreditCards.Add(foreign);
        await t.Db.SaveChangesAsync();

        var result = await Controller(t).Create(Dto(foreign.Id, 100m, 1, 1, 100m));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0.00m, foreign.UsedLimit);
    }

    [Fact]
    public async Task Conta_de_cartao_sem_PolpAccountId_e_sem_usado_conhecido_comeca_de_zero_e_soma_700()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: null);

        await Controller(t).Create(Dto(null, 1200m, 12, 6, 100m, account.Id));

        Assert.Equal(700.00m, account.UsedLimit);
    }

    [Fact]
    public async Task Conta_de_cartao_sem_PolpAccountId_e_sem_limite_conhecido_aceita_qualquer_valor_e_soma_ao_usado()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao", type: "CREDIT_CARD", creditLimit: null, usedLimit: 10m);

        var result = await Controller(t).Create(Dto(null, 1200m, 12, 1, 100m, account.Id));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1210.00m, account.UsedLimit);
    }

    [Fact]
    public async Task Conta_que_nao_e_cartao_e_recusada()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Corrente", type: "BANK", creditLimit: 5000m, usedLimit: 0m);

        var result = await Controller(t).Create(Dto(null, 100m, 1, 1, 100m, account.Id));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Excluir_parcelamento_na_1a_de_12_libera_1200()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, new DateTime(2099, 1, 10));

        await Controller(t).Delete(installment.Id);

        Assert.Equal(0.00m, card.UsedLimit);
        Assert.Empty(await t.Db.Installments.ToListAsync());
    }

    [Fact]
    public async Task Excluir_parcelamento_na_6a_de_12_libera_700_e_preserva_outras_compras()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1000m);
        var installment = t.AddInstallment(card, null, 100m, 6, 12, new DateTime(2099, 1, 10));

        await Controller(t).Delete(installment.Id);

        Assert.Equal(300.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Excluir_com_usado_menor_que_o_comprometido_para_no_piso_zero()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 300m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, new DateTime(2099, 1, 10));

        await Controller(t).Delete(installment.Id);

        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Excluir_parcelamento_inexistente_ou_de_outro_usuario_retorna_404()
    {
        using var t = new TestDb();

        var result = await Controller(t).Delete(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Listar_avanca_parcelas_vencidas_e_retorna_a_6a_de_12_com_usado_700()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        var result = await Controller(t).GetAll();

        var list = Results.OkValue<IEnumerable<InstallmentDto>>(result).ToList();
        Assert.Single(list);
        Assert.Equal(6, list[0].CurrentInstallment);
        Assert.Equal(100.00m, list[0].InstallmentAmount);
        Assert.Equal(700.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Listar_nao_mostra_parcelamento_quitado_e_libera_o_restante()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 700m);
        t.AddInstallment(card, null, 100m, 6, 12, Dates.Day15MonthsAgo(7));

        var result = await Controller(t).GetAll();

        Assert.Empty(Results.OkValue<IEnumerable<InstallmentDto>>(result));
        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public void Disponivel_do_cartao_e_limite_menos_usado_com_centavos()
    {
        var card = new ControlFinance.API.Models.CreditCard { Name = "C", CreditLimit = 5000.00m, UsedLimit = 1250.75m };

        Assert.Equal(3749.25m, CreditCardDto.FromEntity(card).AvailableLimit);
    }

    [Fact]
    public void Disponivel_do_cartao_com_usado_zero_e_o_limite_inteiro()
    {
        var card = new ControlFinance.API.Models.CreditCard { Name = "C", CreditLimit = 800.00m, UsedLimit = 0m };

        Assert.Equal(800.00m, CreditCardDto.FromEntity(card).AvailableLimit);
    }

    [Fact]
    public void Disponivel_do_cartao_com_usado_acima_do_limite_fica_negativo_menos_200()
    {
        var card = new ControlFinance.API.Models.CreditCard { Name = "C", CreditLimit = 1000m, UsedLimit = 1200m };

        Assert.Equal(-200.00m, CreditCardDto.FromEntity(card).AvailableLimit);
    }

    [Theory]
    [InlineData(2099, 1, 31, 31)]
    [InlineData(2099, 2, 28, 28)]
    [InlineData(2099, 6, 10, 10)]
    public async Task Criar_grava_DueDay_igual_ao_dia_da_primeira_data_de_vencimento(int y, int m, int d, int expected)
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);
        var dto = new CreateInstallmentDto(card.Id, null, "Notebook", 1200m, 12, 1, 100m, new DateTime(y, m, d));

        await Controller(t).Create(dto);

        await using var other = t.NewContext();
        Assert.Equal(expected, (await other.Installments.SingleAsync()).DueDay);
    }

    [Fact]
    public async Task Cartao_sincronizado_Polp_criar_nao_altera_o_usado_e_pula_validacao_de_limite_excedido()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Polp", type: "CREDIT_CARD", creditLimit: 1000m, usedLimit: 1000m, polpAccountId: Guid.NewGuid());

        var result = await Controller(t).Create(Dto(null, 1200m, 12, 1, 100m, account.Id));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1000.00m, account.UsedLimit);
        Assert.Single(await t.Db.Installments.ToListAsync());
    }

    [Fact]
    public async Task Cartao_sincronizado_Polp_com_usado_nulo_continua_nulo_apos_criar()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Polp", type: "CREDIT_CARD", creditLimit: 1000m, usedLimit: null, polpAccountId: Guid.NewGuid());

        await Controller(t).Create(Dto(null, 1200m, 12, 1, 100m, account.Id));

        Assert.Null(account.UsedLimit);
    }

    [Fact]
    public async Task Cartao_sincronizado_Polp_excluir_nao_altera_o_usado()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Polp", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 1200m, polpAccountId: Guid.NewGuid());
        var installment = t.AddInstallment(null, account, 100m, 1, 12, new DateTime(2099, 1, 10));

        await Controller(t).Delete(installment.Id);

        Assert.Equal(1200.00m, account.UsedLimit);
        Assert.Empty(await t.Db.Installments.ToListAsync());
    }

    [Fact]
    public async Task Lado_a_lado_criar_12x100_manual_soma_1200_e_sincronizado_fica_igual()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 0m);
        var synced = t.AddAccount("Cartao Polp", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 300m, polpAccountId: Guid.NewGuid());
        var plain = t.AddAccount("Conta cartao", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 300m);
        var controller = Controller(t);

        await controller.Create(Dto(card.Id, 1200m, 12, 1, 100m));
        await controller.Create(Dto(null, 1200m, 12, 1, 100m, synced.Id));
        await controller.Create(Dto(null, 1200m, 12, 1, 100m, plain.Id));

        Assert.Equal(1200.00m, card.UsedLimit);
        Assert.Equal(300.00m, synced.UsedLimit);
        Assert.Equal(1500.00m, plain.UsedLimit);
    }
}
