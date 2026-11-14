using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class TransactionDetailTests
{
    private static (TestDb T, ScriptedPolp Polp, BankAccount Account) Setup(bool card)
    {
        var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var account = SyncHarness.AddLinked(t, integration, Guid.NewGuid(), card);
        var polp = new ScriptedPolp();
        if (card) polp.Bills[account.PolpAccountId!.Value] = [];
        return (t, polp, account);
    }

    private static Task Sync(TestDb t, ScriptedPolp polp, BankAccount account, bool card) =>
        SyncHarness.Processor(t, polp).Process(card ? "credit_cards.transactions" : "accounts.transactions", account.PolpAccountId!.Value);

    private static async Task<TransactionDetailDto> Detail(TestDb t, Guid id) =>
        Results.OkValue<TransactionDetailDto>(await t.Controller(db => new TransactionsController(db)).GetById(id, default));

    [Theory]
    [InlineData("2026-10-01", null)]
    [InlineData("2026-10-01T00:00:00Z", null)]
    [InlineData("2026-10-01T00:00:00-03:00", null)]
    [InlineData("2026-10-01T15:30:00Z", "2026-10-01T15:30:00")]
    [InlineData("2026-10-01T15:30:00-03:00", "2026-10-01T18:30:00")]
    [InlineData("2026-10-01T15:30:00", "2026-10-01T18:30:00")]
    [InlineData("lixo", null)]
    public void Horario_do_banco_so_existe_quando_o_banco_manda_horario(string text, string? expectedUtc)
    {
        var instant = PolpSyncService.ToBankInstant(text);
        if (expectedUtc is null)
        {
            Assert.Null(instant);
            return;
        }
        Assert.Equal(DateTime.Parse(expectedUtc), instant);
        Assert.Equal(DateTimeKind.Utc, instant!.Value.Kind);
    }

    [Fact]
    public async Task Transacao_de_conta_guarda_dados_do_banco_mascarados_e_o_detalhe_devolve()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T15:30:00Z", "-80.00");
        tx.AccountId = account.PolpAccountId!.Value;
        tx.TransactionName = "PIX ENVIADO FULANO";
        tx.Type = "PIX";
        tx.PartieCompeCode = "341";
        tx.PartieNumber = "0012345-6";
        tx.Counterparty = new PolpCounterpartyDto { Name = "EMPRESA LTDA", Alias = "Empresa", TaxId = "12345678000199" };
        tx.PartiePersonType = "PESSOA_JURIDICA";
        tx.CreditDebitType = "DEBITO";
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];

        await Sync(t, polp, account, card: false);

        var id = t.NewContext().Transactions.Single().Id;
        var detail = await Detail(t, id);
        Assert.Equal("Expense", detail.Type);
        Assert.Equal(80m, detail.Amount);
        Assert.NotNull(detail.Bank);
        Assert.Equal(new DateTime(2026, 10, 1, 15, 30, 0, DateTimeKind.Utc), detail.Bank!.DateTime);
        Assert.Equal("PIX", detail.Bank.OperationType);
        Assert.Equal("PIX ENVIADO FULANO", detail.Bank.Description);
        Assert.Equal("Empresa", detail.Bank.CounterpartyAlias);
        Assert.Equal("EMPRESA LTDA", detail.Bank.CounterpartyLegalName);
        Assert.Null(detail.Bank.DescribedParty);
        Assert.Equal("12345678000199", detail.Bank.CounterpartyCnpj);
        Assert.Equal("PESSOA_JURIDICA", detail.Bank.CounterpartyPersonType);
        Assert.Equal("ready", detail.Bank.DetailsStatus);
        Assert.Equal("341", detail.Bank.CounterpartyBankCode);
        Assert.Equal("•••• 3456", detail.Bank.CounterpartyAccount);
        Assert.Null(detail.Bank.CardNumber);
        Assert.Null(detail.Bank.BillMonth);
        Assert.False(detail.Account!.IsCard);
    }

    [Fact]
    public async Task Compra_parcelada_internacional_no_cartao_guarda_parcela_moeda_cartao_e_fatura()
    {
        var (t, polp, account) = Setup(card: true);
        using var _t = t;
        var tx = ScriptedPolp.CardTx("2026-10-01T10:00:00Z", "100.00", billId: Guid.NewGuid(), forecast: "2026-11", plastic: "5555444433332222");
        tx.CreditCardId = account.PolpAccountId!.Value;
        tx.ChargeIdentificator = 2;
        tx.ChargeNumber = 10;
        tx.OriginalAmount = new PolpMoney { Amount = "18.50", Currency = "usd" };
        polp.CardTransactions[account.PolpAccountId!.Value] = [tx];

        await Sync(t, polp, account, card: true);

        var row = t.NewContext().Transactions.Single();
        Assert.Equal(2, row.InstallmentNumber);
        Assert.Equal(10, row.InstallmentTotal);
        Assert.Equal(18.50m, row.OriginalAmount);
        Assert.Equal("USD", row.OriginalCurrency);
        Assert.Equal("•••• 2222", row.CardNumber);
        Assert.Equal("2026-11", row.BillMonth);
        Assert.Equal("PAGAMENTO", row.BankOperationType);
    }

    [Fact]
    public async Task Valor_em_reais_e_parcela_invalida_nao_viram_dado_extra()
    {
        var (t, polp, account) = Setup(card: true);
        using var _t = t;
        var tx = ScriptedPolp.CardTx("2026-10-01T10:00:00Z", "100.00", billId: Guid.NewGuid());
        tx.CreditCardId = account.PolpAccountId!.Value;
        tx.ChargeIdentificator = 3;
        tx.ChargeNumber = 2;
        tx.OriginalAmount = new PolpMoney { Amount = "100.00", Currency = "BRL" };
        polp.CardTransactions[account.PolpAccountId!.Value] = [tx];

        await Sync(t, polp, account, card: true);

        var row = t.NewContext().Transactions.Single();
        Assert.Null(row.InstallmentNumber);
        Assert.Null(row.InstallmentTotal);
        Assert.Null(row.OriginalAmount);
        Assert.Null(row.OriginalCurrency);
    }

    [Fact]
    public async Task Documento_de_pessoa_fisica_e_tipo_de_pessoa_desconhecido_nao_sao_guardados()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T12:00:00Z", "10.00");
        tx.AccountId = account.PolpAccountId!.Value;
        tx.Counterparty = new PolpCounterpartyDto { Name = "Fulano", TaxId = "12345678901" };
        tx.PartiePersonType = "OUTRO";
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];

        await Sync(t, polp, account, card: false);

        var row = t.NewContext().Transactions.Single();
        Assert.Null(row.CounterpartyCnpj);
        Assert.Null(row.CounterpartyPersonType);
    }

    [Fact]
    public async Task Webhook_sem_contraparte_nao_apaga_a_que_ja_chegou()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T12:00:00Z", "10.00");
        tx.AccountId = account.PolpAccountId!.Value;
        tx.Counterparty = new PolpCounterpartyDto { Alias = "Mercado Bom" };
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];
        await Sync(t, polp, account, card: false);

        tx.Counterparty = null;
        await Sync(t, polp, account, card: false);

        Assert.Equal("Mercado Bom", t.NewContext().Transactions.Single().CounterpartyAlias);
    }

    [Fact]
    public async Task Pix_para_pessoa_sem_contraparte_usa_o_nome_da_descricao_original_e_nao_o_nome_editado()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T12:00:00Z", "50.00");
        tx.AccountId = account.PolpAccountId!.Value;
        tx.TransactionName = "PIX ENVIADO LUCAS PEREIRA";
        tx.Type = "PIX";
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];
        await Sync(t, polp, account, card: false);

        await using (var db = t.NewContext())
        {
            db.Transactions.Single().Name = "Aluguel";
            await db.SaveChangesAsync();
        }

        var detail = await Detail(t, t.NewContext().Transactions.Single().Id);
        Assert.Equal("LUCAS PEREIRA", detail.Bank.DescribedParty);
        Assert.Null(detail.Bank.CounterpartyAlias);
        Assert.Null(detail.Bank.CounterpartyLegalName);
    }

    [Fact]
    public async Task Compra_no_cartao_sem_contraparte_usa_a_descricao_da_fatura()
    {
        var (t, polp, account) = Setup(card: true);
        using var _t = t;
        var tx = ScriptedPolp.CardTx("2026-10-01T10:00:00Z", "300.00", billId: Guid.NewGuid());
        tx.CreditCardId = account.PolpAccountId!.Value;
        tx.TransactionName = "MAGAZINE LUIZA 03/10";
        polp.CardTransactions[account.PolpAccountId!.Value] = [tx];
        await Sync(t, polp, account, card: true);

        var detail = await Detail(t, t.NewContext().Transactions.Single().Id);
        Assert.Equal("MAGAZINE LUIZA", detail.Bank.DescribedParty);
    }

    [Fact]
    public async Task Linha_importada_antes_dos_detalhes_aparece_como_pendente_e_a_releitura_preenche_uma_vez()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T12:00:00Z", "10.00");
        tx.AccountId = account.PolpAccountId!.Value;
        tx.Counterparty = new PolpCounterpartyDto { Name = "UBER DO BRASIL TECNOLOGIA LTDA." };
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];
        await Sync(t, polp, account, card: false);

        await using (var db = t.NewContext())
        {
            var row = db.Transactions.Single();
            row.BankDescription = null;
            row.BankOperationType = null;
            row.CounterpartyName = null;
            await db.SaveChangesAsync();
        }
        var id = t.NewContext().Transactions.Single().Id;
        Assert.Equal("importing", (await Detail(t, id)).Bank.DetailsStatus);

        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.TransactionDetails, t.NewContext().PolpIntegrations.Single().PolpIntegrationId);

        var detail = await Detail(t, id);
        Assert.Equal("ready", detail.Bank.DetailsStatus);
        Assert.Equal("UBER DO BRASIL TECNOLOGIA LTDA.", detail.Bank.CounterpartyLegalName);
        Assert.Null(detail.Bank.CounterpartyAlias);
        Assert.Equal(PolpSyncService.TransactionDetailsVersion, t.NewContext().PolpIntegrations.Single().TransactionDetailsVersion);

        var calls = polp.Calls.Count;
        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.TransactionDetails, t.NewContext().PolpIntegrations.Single().PolpIntegrationId);
        Assert.Equal(calls, polp.Calls.Count);
    }

    [Fact]
    public async Task Releitura_falhando_aparece_como_atrasada_e_concluida_sem_dados_deixa_de_ser_pendente()
    {
        var (t, polp, account) = Setup(card: false);
        using var _t = t;
        var tx = ScriptedPolp.AccountTx("2026-10-01T12:00:00Z", "10.00");
        tx.AccountId = account.PolpAccountId!.Value;
        polp.AccountTransactions[account.PolpAccountId!.Value] = [tx];
        await Sync(t, polp, account, card: false);

        await using (var db = t.NewContext())
        {
            var row = db.Transactions.Single();
            row.BankDescription = null;
            row.BankOperationType = null;
            var consentId = db.PolpIntegrations.Single().PolpIntegrationId;
            db.PolpWebhookEvents.Add(new PolpWebhookEvent
            {
                Event = PolpInternalEvents.TransactionDetails, Resource = "consents", ResourceId = consentId.ToString(),
                DedupKey = "k", Status = PolpWebhookEventStatus.Pending, Attempts = 2, LastError = "HttpRequestException HTTP 401",
                NextAttemptAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var id = t.NewContext().Transactions.Single().Id;
        Assert.Equal("delayed", (await Detail(t, id)).Bank.DetailsStatus);

        await using (var db = t.NewContext())
        {
            db.PolpIntegrations.Single().TransactionDetailsVersion = PolpSyncService.TransactionDetailsVersion;
            await db.SaveChangesAsync();
        }
        var detail = await Detail(t, id);
        Assert.Equal("ready", detail.Bank.DetailsStatus);
        Assert.Null(detail.Bank.DescribedParty);
    }

    [Fact]
    public async Task Transacao_sem_vinculo_com_a_Polp_responde_404()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var row = t.AddTransaction(account, TransactionType.Expense, 30m, Dates.Today);

        var result = await t.Controller(db => new TransactionsController(db)).GetById(row.Id, default);

        Assert.Equal(404, Http.Status(result));
    }

    [Fact]
    public async Task Detalhe_de_transacao_alheia_responde_404()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);

        var result = await t.Controller(db => new TransactionsController(db)).GetById(other.Transaction.Id, default);

        Assert.Equal(404, Http.Status(result));
    }
}

public class DescribedCounterpartyTests
{
    [Theory]
    [InlineData("PIX ENVIADO LUCAS PEREIRA", false, "LUCAS PEREIRA")]
    [InlineData("Pix enviado - Lucas Pereira", false, "Lucas Pereira")]
    [InlineData("PIX RECEBIDO DE MARIA SOUZA 12/10", true, "MARIA SOUZA")]
    [InlineData("TED ENVIADA PARA JOÃO DA SILVA", false, "JOÃO DA SILVA")]
    [InlineData("Transferência Recebida|Ana Lima", true, "Ana Lima")]
    [InlineData("PIX ENVIADO DEBORA ***.456.789-**", false, "DEBORA")]
    [InlineData("PIX ENVIADO LUCAS PEREIRA", true, null)]
    [InlineData("PIX RECEBIDO MARIA", false, null)]
    [InlineData("PIX QR CODE ESTATICO", false, null)]
    [InlineData("PIX ENVIADO", false, null)]
    [InlineData("PAGAMENTO DE BOLETO", false, null)]
    [InlineData("COMPRA CARTAO 1234", false, null)]
    [InlineData("PIX ENVIADO 12345678900", false, null)]
    [InlineData("", false, null)]
    public void Conta_so_extrai_nome_de_padrao_reconhecivel_e_coerente_com_a_direcao(string text, bool credit, string? expected) =>
        Assert.Equal(expected, DescribedCounterparty.FromAccount(text, credit));

    [Theory]
    [InlineData("MAGAZINE LUIZA 03/10", "MAGAZINE LUIZA")]
    [InlineData("UBER *TRIP", "UBER *TRIP")]
    [InlineData("NETFLIX.COM PARC 02/12", "NETFLIX.COM")]
    [InlineData("1234", null)]
    public void Cartao_usa_a_descricao_da_fatura_sem_o_sufixo_de_parcela(string text, string? expected) =>
        Assert.Equal(expected, DescribedCounterparty.FromCard(text));
}
