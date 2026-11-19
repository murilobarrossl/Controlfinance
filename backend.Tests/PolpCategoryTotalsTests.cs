using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class PolpCategoryTotalsTests
{
    private const string CardBillPayment = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";
    private const string SelfTransferOut = "TRANSFER_OUT_ACCOUNT_TRANSFER";
    private const string SelfTransferIn = "TRANSFER_IN_ACCOUNT_TRANSFER";

    private static DateTime MonthStart(int offset = 0)
    {
        var now = Dates.BrazilNow();
        return new DateTime(now.Year, now.Month, 1).AddMonths(offset);
    }

    private static async Task<DashboardSummaryDto> Summary(TestDb t, Guid accountId) =>
        Results.OkValue<DashboardSummaryDto>(await t.Controller(db => new DashboardController(db)).GetSummary(accountId, default));

    private static async Task<TransactionAggregatesDto> MonthAggregates(TestDb t, Guid? accountId = null) =>
        Results.OkValue<TransactionAggregatesDto>(await t.Controller(db => new TransactionsController(db))
            .GetAggregates(accountId, MonthStart().ToString("yyyy-MM-dd"), MonthStart(1).ToString("yyyy-MM-dd"), default));

    private static async Task<TransactionReportDto> Report(TestDb t, Guid? accountId, string sortBy = "dueDate") =>
        Results.OkValue<TransactionReportDto>(await t.Controller(db => new TransactionsController(db))
            .GetReport(null, null, null, accountId, sortBy: sortBy));

    private static async Task<List<TransactionDto>> All(TestDb t, string? status = null, string? type = null) =>
        Results.OkValue<List<TransactionDto>>(await t.Controller(db => new TransactionsController(db)).GetAll(status, type, null));

    private static async Task<List<TransactionDto>> MonthDetails(TestDb t, string? category) =>
        Results.OkValue<List<TransactionDto>>(await t.Controller(db => new TransactionsController(db))
            .GetDetails(null, MonthStart().ToString("yyyy-MM-dd"), MonthStart(1).ToString("yyyy-MM-dd"), null, category, null, default));

    // Mesmo resultado que o sync grava: categoria pelo mapa e o category_ref original.
    private static Transaction Synced(TestDb t, BankAccount account, TransactionType type, decimal amount, DateTime day,
        string name, string categoryRef, TransactionStatus status = TransactionStatus.Paid)
    {
        var categoryName = PolpCategoryMap.ToCategoryName(categoryRef);
        var category = t.Db.Categories.SingleOrDefault(c => c.UserId == t.User.Id && c.Name == categoryName)
            ?? t.AddCategory(categoryName);
        return t.AddTransaction(account, type, amount, day, name, status, category, TransferDetection.ToMovementKind(categoryRef));
    }

    [Fact]
    public void Pagamento_de_fatura_tem_categoria_propria_e_parcela_e_emprestimo_recebido_vao_para_Emprestimo()
    {
        Assert.Equal("Pagamento de fatura", PolpCategoryMap.ToCategoryName("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"));
        Assert.Equal("Empréstimo", PolpCategoryMap.ToCategoryName("LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT"));
        Assert.Equal("Empréstimo", PolpCategoryMap.ToCategoryName("LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT"));
    }

    [Fact]
    public async Task Cpf_ve_emprestimo_recebido_como_receita_e_parcelas_como_despesa_na_categoria_Emprestimo()
    {
        using var t = new TestDb();
        var checking = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(4);
        Synced(t, checking, TransactionType.Income, 3000m, day, "SALARIO", "INCOME_SALARY");
        Synced(t, checking, TransactionType.Income, 10000m, day, "CREDITO EMPRESTIMO", "LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT");
        Synced(t, checking, TransactionType.Income, 2500.50m, day.AddDays(1), "CREDITO CONSIGNADO", "LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT");
        Synced(t, checking, TransactionType.Expense, 450m, day.AddDays(2), "PARCELA EMPRESTIMO", "LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT");
        var legacy = t.AddCategory("Empréstimos");
        t.AddTransaction(checking, TransactionType.Expense, 120.25m, day.AddDays(3), "PARCELA ANTIGA", TransactionStatus.Paid, legacy);

        var all = await MonthAggregates(t);
        Assert.Equal(15500.50m, all.TotalIncome);
        Assert.Equal(570.25m, all.TotalExpense);
        Assert.Contains(all.Categories, c => c.CategoryName == "Empréstimo" && c.Type == "Income" && c.Amount == 12500.50m && c.Count == 2);
        Assert.Contains(all.Categories, c => c.CategoryName == "Empréstimo" && c.Type == "Expense" && c.Amount == 570.25m && c.Count == 2);
        Assert.DoesNotContain(all.Categories, c => c.CategoryName == "Empréstimos");

        var summary = await Summary(t, checking.Id);
        Assert.Equal(15500.50m, summary.TotalIncome);
        Assert.Equal(570.25m, Assert.Single(summary.CategoryExpenses, c => c.CategoryName == "Empréstimo").Amount);

        var details = await MonthDetails(t, "Empréstimo");
        Assert.Equal(4, details.Count);
        Assert.All(details, d => Assert.False(d.IsTransfer));
    }

    [Fact]
    public async Task Cnpj_continua_sem_emprestimo_recebido_nos_totais()
    {
        using var t = new TestDb();
        t.User.DocumentType = DocumentTypes.Cnpj;
        t.Db.SaveChanges();
        var checking = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(4);
        Synced(t, checking, TransactionType.Income, 10000m, day, "CREDITO EMPRESTIMO", "LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT");
        Synced(t, checking, TransactionType.Expense, 450m, day, "PARCELA EMPRESTIMO", "LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT");

        var all = await MonthAggregates(t);
        Assert.Equal(0m, all.TotalIncome);
        Assert.Equal(450m, all.TotalExpense);
        Assert.Equal(0m, (await Summary(t, checking.Id)).TotalIncome);
        Assert.Single(await MonthDetails(t, "Empréstimo"));
    }

    [Fact]
    public void Transferencias_entre_contas_proprias_viram_mesma_titularidade_e_as_demais_continuam_Transferencias()
    {
        Assert.Equal("Transferência mesma titularidade", PolpCategoryMap.ToCategoryName("TRANSFER_IN_ACCOUNT_TRANSFER"));
        Assert.Equal("Transferência mesma titularidade", PolpCategoryMap.ToCategoryName("TRANSFER_OUT_ACCOUNT_TRANSFER"));
        Assert.Equal("Transferências", PolpCategoryMap.ToCategoryName("TRANSFER_OUT_WITHDRAWAL"));
        Assert.Equal("Transferências", PolpCategoryMap.ToCategoryName("TRANSFER_IN_DEPOSIT"));
    }

    [Fact]
    public async Task Despesa_do_mes_conta_as_compras_do_cartao_uma_vez_so_e_o_pagamento_da_fatura_continua_no_relatorio()
    {
        using var t = new TestDb();
        var checking = t.AddAccount("Conta corrente", balance: 1000m, type: "CONTA_DEPOSITO_A_VISTA", polpAccountId: Guid.NewGuid());
        var card = t.AddAccount("Cartão", type: CreditCardAccountDetector.CardType, creditLimit: 5000m, polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(9);
        Synced(t, card, TransactionType.Expense, 320.45m, day, "MERCADO CENTRAL", "FOOD_AND_DRINK_GROCERIES");
        Synced(t, card, TransactionType.Expense, 179.55m, day, "POSTO SHELL", "TRANSPORTATION_GAS");
        t.AddTransaction(checking, TransactionType.Expense, 25.10m, day, "PADARIA");
        var payment = Synced(t, checking, TransactionType.Expense, 500m, day.AddDays(1), "PAGAMENTO FATURA CARTAO", CardBillPayment);

        var all = await MonthAggregates(t);
        Assert.Equal(525.10m, all.TotalExpense);
        Assert.Equal(0m, all.TotalIncome);
        Assert.Equal(525.10m, all.Months.Single().Expense);
        Assert.DoesNotContain(all.Categories, c => c.CategoryName == "Pagamento de fatura" || c.CategoryName == "Empréstimos");

        Assert.Equal(25.10m, (await Summary(t, checking.Id)).TotalExpense);
        Assert.Equal(500m, (await Summary(t, card.Id)).TotalExpense);

        var report = await Report(t, checking.Id);
        Assert.Equal(25.10m, report.TotalExpense);
        Assert.Equal(2, report.TotalCount);
        var listed = Assert.Single(report.Items, i => i.Id == payment.Id);
        Assert.True(listed.IsTransfer);
        Assert.Equal(500m, listed.Amount);
        Assert.Equal("Pagamento de fatura", listed.CategoryName);
    }

    [Fact]
    public async Task Transferencia_entre_contas_do_mesmo_titular_nao_altera_receita_nem_despesa()
    {
        using var t = new TestDb();
        var a = t.AddAccount("Banco A", polpAccountId: Guid.NewGuid());
        var b = t.AddAccount("Banco B", polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(9);
        Synced(t, a, TransactionType.Income, 3000m, day, "SALARIO", "INCOME_SALARY");
        Synced(t, a, TransactionType.Expense, 120.30m, day, "FARMACIA", "MEDICAL_PHARMACIES_AND_SUPPLEMENTS");
        Synced(t, b, TransactionType.Expense, 45.70m, day, "UBER", "TRANSPORTATION_TAXIS_AND_RIDE_SHARES");
        Synced(t, a, TransactionType.Expense, 2000m, day.AddDays(1), "TRANSFERENCIA MESMA TITULARIDADE", SelfTransferOut);
        Synced(t, b, TransactionType.Income, 2000m, day.AddDays(1), "TRANSFERENCIA MESMA TITULARIDADE", SelfTransferIn);

        var all = await MonthAggregates(t);
        Assert.Equal(3000m, all.TotalIncome);
        Assert.Equal(166.00m, all.TotalExpense);

        var summaryA = await Summary(t, a.Id);
        Assert.Equal(3000m, summaryA.TotalIncome);
        Assert.Equal(120.30m, summaryA.TotalExpense);

        var summaryB = await Summary(t, b.Id);
        Assert.Equal(0m, summaryB.TotalIncome);
        Assert.Equal(45.70m, summaryB.TotalExpense);
    }

    [Fact]
    public async Task Pagamento_de_fatura_e_transferencia_propria_ficam_fora_de_todos_os_totais_e_continuam_listados_como_transferencia()
    {
        using var t = new TestDb();
        var checking = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var savings = t.AddAccount("Poupança", polpAccountId: Guid.NewGuid());
        var card = t.AddAccount("Cartão", type: CreditCardAccountDetector.CardType, creditLimit: 5000m, polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(9);
        Synced(t, checking, TransactionType.Income, 3000m, day, "SALARIO", "INCOME_SALARY");
        Synced(t, checking, TransactionType.Expense, 25.10m, day, "PADARIA", "FOOD_AND_DRINK_GROCERIES");
        Synced(t, card, TransactionType.Expense, 320.45m, day, "MERCADO CENTRAL", "FOOD_AND_DRINK_GROCERIES");
        Synced(t, card, TransactionType.Expense, 179.55m, day, "POSTO SHELL", "TRANSPORTATION_GAS");
        var payment = Synced(t, checking, TransactionType.Expense, 500m, day.AddDays(1), "PAGAMENTO FATURA CARTAO", CardBillPayment);
        var transferOut = Synced(t, checking, TransactionType.Expense, 1000m, day.AddDays(1), "PIX ENVIADO", SelfTransferOut);
        var transferIn = Synced(t, savings, TransactionType.Income, 1000m, day.AddDays(1), "PIX RECEBIDO", SelfTransferIn);
        var selfIds = new[] { payment.Id, transferOut.Id, transferIn.Id };

        var all = await MonthAggregates(t);
        Assert.Equal(3000m, all.TotalIncome);
        Assert.Equal(525.10m, all.TotalExpense);
        Assert.Equal(4, all.Count);
        Assert.Equal(3000m, all.Months.Single().Income);
        Assert.Equal(525.10m, all.Months.Single().Expense);
        Assert.DoesNotContain(all.Categories, c => c.CategoryName is "Pagamento de fatura" or "Transferência mesma titularidade");
        Assert.DoesNotContain(all.Establishments, e => e.Key is "PAGAMENTO FATURA CARTAO" or "PIX ENVIADO" or "PIX RECEBIDO");

        var checkingAggregates = await MonthAggregates(t, checking.Id);
        Assert.Equal(3000m, checkingAggregates.TotalIncome);
        Assert.Equal(25.10m, checkingAggregates.TotalExpense);
        var savingsAggregates = await MonthAggregates(t, savings.Id);
        Assert.Equal(0m, savingsAggregates.TotalIncome);
        Assert.Equal(0m, savingsAggregates.TotalExpense);

        var checkingSummary = await Summary(t, checking.Id);
        Assert.Equal(3000m, checkingSummary.TotalIncome);
        Assert.Equal(25.10m, checkingSummary.TotalExpense);
        Assert.DoesNotContain(checkingSummary.CategoryExpenses, c => c.CategoryName is "Pagamento de fatura" or "Transferência mesma titularidade");
        var savingsSummary = await Summary(t, savings.Id);
        Assert.Equal(0m, savingsSummary.TotalIncome);
        Assert.Equal(0m, savingsSummary.TotalExpense);
        Assert.Equal(500m, (await Summary(t, card.Id)).TotalExpense);

        foreach (var sortBy in new[] { "dueDate", "amount" })
        {
            var report = await Report(t, null, sortBy);
            Assert.Equal(3000m, report.TotalIncome);
            Assert.Equal(525.10m, report.TotalExpense);
            Assert.Equal(7, report.TotalCount);
            Assert.All(report.Items, i => Assert.Equal(selfIds.Contains(i.Id), i.IsTransfer));
        }

        var listed = await All(t);
        Assert.Equal(7, listed.Count);
        Assert.All(listed, i => Assert.Equal(selfIds.Contains(i.Id), i.IsTransfer));

        Assert.Empty(await MonthDetails(t, "Pagamento de fatura"));
        Assert.Empty(await MonthDetails(t, "Transferência mesma titularidade"));
        Assert.Equal(4, (await MonthDetails(t, null)).Count);
    }

    [Fact]
    public async Task Renomear_a_categoria_e_recategorizar_a_transacao_nao_muda_os_totais()
    {
        using var t = new TestDb();
        var checking = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var savings = t.AddAccount("Poupança", polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(9);
        Synced(t, checking, TransactionType.Expense, 25.10m, day, "PADARIA", "FOOD_AND_DRINK_GROCERIES");
        var payment = Synced(t, checking, TransactionType.Expense, 500m, day, "PAGAMENTO FATURA CARTAO", CardBillPayment);
        var transferOut = Synced(t, checking, TransactionType.Expense, 1000m, day, "PIX ENVIADO", SelfTransferOut);
        var transferIn = Synced(t, savings, TransactionType.Income, 1000m, day, "PIX RECEBIDO", SelfTransferIn);
        var food = t.Db.Categories.Single(c => c.Name == "Alimentação");

        async Task AssertTotals()
        {
            var all = await MonthAggregates(t);
            Assert.Equal(0m, all.TotalIncome);
            Assert.Equal(25.10m, all.TotalExpense);
            Assert.Equal(25.10m, Assert.Single(all.Categories).Amount);
            Assert.Equal(25.10m, (await Summary(t, checking.Id)).TotalExpense);
            Assert.Equal(0m, (await Summary(t, savings.Id)).TotalIncome);
            var report = await Report(t, null);
            Assert.Equal(0m, report.TotalIncome);
            Assert.Equal(25.10m, report.TotalExpense);
        }

        await AssertTotals();

        var categories = t.Controller(db => new CategoriesController(db));
        Results.OkValue<CategoryDto>(await categories.Update(payment.CategoryId!.Value, new CreateCategoryDto("Cartão", null)));

        var transactions = t.Controller(db => new TransactionsController(db));
        Results.OkValue<TransactionDto>(await transactions.SetDetails(transferOut.Id, new SetTransactionDetailsDto("Mercado", food.Id)));
        Results.OkValue<TransactionDto>(await transactions.SetDetails(transferIn.Id, new SetTransactionDetailsDto("Salário extra", food.Id)));

        await AssertTotals();

        var listed = await All(t);
        Assert.True(listed.Single(i => i.Id == payment.Id).IsTransfer);
        Assert.Equal("Cartão", listed.Single(i => i.Id == payment.Id).CategoryName);
        Assert.True(listed.Single(i => i.Id == transferOut.Id).IsTransfer);
        Assert.Equal("Alimentação", listed.Single(i => i.Id == transferOut.Id).CategoryName);
        Assert.True(listed.Single(i => i.Id == transferIn.Id).IsTransfer);
    }

    [Fact]
    public async Task Lancamento_manual_em_categoria_com_nome_de_pagamento_de_fatura_ou_mesma_titularidade_conta_normalmente()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Conta manual");
        var billCategory = t.AddCategory(TransferDetection.CardBillPaymentCategoryName);
        var selfCategory = t.AddCategory(TransferDetection.SelfTransferCategoryName);
        var day = MonthStart().AddDays(9);
        // Lançamentos manuais antigos (não há mais como criar): gravados direto, como ficaram no banco.
        t.AddTransaction(account, TransactionType.Expense, 500m, day, "Fatura do cartão", category: billCategory);
        t.AddTransaction(account, TransactionType.Expense, 800m, day, "Pix pra poupança", category: selfCategory);
        t.AddTransaction(account, TransactionType.Income, 120m, day, "Reembolso", category: selfCategory);
        Assert.All(t.Db.Transactions.ToList(), x => Assert.Equal(PolpMovementKind.None, x.PolpMovementKind));

        var all = await MonthAggregates(t);
        Assert.Equal(120m, all.TotalIncome);
        Assert.Equal(1300m, all.TotalExpense);
        Assert.Contains(all.Categories, c => c.CategoryName == "Pagamento de fatura" && c.Amount == 500m);
        Assert.Contains(all.Categories, c => c.CategoryName == "Transferência mesma titularidade" && c.Type == "Expense" && c.Amount == 800m);

        var summary = await Summary(t, account.Id);
        Assert.Equal(120m, summary.TotalIncome);
        Assert.Equal(1300m, summary.TotalExpense);

        var report = await Report(t, account.Id);
        Assert.Equal(120m, report.TotalIncome);
        Assert.Equal(1300m, report.TotalExpense);
        Assert.All(report.Items, i => Assert.False(i.IsTransfer));
        Assert.Single(await MonthDetails(t, "Pagamento de fatura"));
    }

    [Fact]
    public async Task Transferencia_comum_para_terceiros_conta_normalmente()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var day = MonthStart().AddDays(9);
        var sent = Synced(t, account, TransactionType.Expense, 250m, day, "PIX ENVIADO JOAO", "TRANSFER_OUT_WITHDRAWAL");
        Synced(t, account, TransactionType.Income, 400m, day, "PIX RECEBIDO MARIA", "TRANSFER_IN_DEPOSIT");

        var all = await MonthAggregates(t);
        Assert.Equal(400m, all.TotalIncome);
        Assert.Equal(250m, all.TotalExpense);

        var summary = await Summary(t, account.Id);
        Assert.Equal(400m, summary.TotalIncome);
        Assert.Equal(250m, summary.TotalExpense);

        var report = await Report(t, account.Id);
        Assert.Equal(400m, report.TotalIncome);
        Assert.Equal(250m, report.TotalExpense);
        Assert.False(report.Items.Single(i => i.Id == sent.Id).IsTransfer);
    }

    [Fact]
    public async Task Proximos_vencimentos_recebem_pagamento_de_fatura_pendente_marcado_como_transferencia()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Conta corrente", polpAccountId: Guid.NewGuid());
        var due = Dates.Today.AddDays(3);
        var payment = Synced(t, account, TransactionType.Expense, 500m, due, "PAGAMENTO FATURA CARTAO", CardBillPayment, TransactionStatus.Pending);
        var bill = Synced(t, account, TransactionType.Expense, 180m, due, "CONTA DE LUZ", "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", TransactionStatus.Pending);

        var pending = await All(t, "Pending", "Expense");

        Assert.Equal(2, pending.Count);
        Assert.True(pending.Single(i => i.Id == payment.Id).IsTransfer);
        Assert.False(pending.Single(i => i.Id == bill.Id).IsTransfer);
    }
}
