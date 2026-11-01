using System.Reflection;
using System.Text.Json;
using ControlFinance.API.Controllers;
using ControlFinance.API.Models;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Tests;

public class AggregatesTests
{
    private static readonly DateTime Jan = new(2025, 1, 1);
    private static readonly DateTime Mar = new(2025, 3, 1);
    private static readonly DateTime From = new(2025, 1, 1);
    private static readonly DateTime To = new(2025, 4, 1);

    private static async Task<JsonElement> Aggregates(TestDb t, Guid? bankAccountId, DateTime? from, DateTime? to)
    {
        var controller = t.Controller(db => new TransactionsController(db));
        var method = typeof(TransactionsController).GetMethods()
            .Single(m => m.GetCustomAttributes<HttpGetAttribute>().Any(a => a.Template == "aggregates"));
        var args = method.GetParameters().Select(p => p.ParameterType == typeof(CancellationToken)
            ? (object?)CancellationToken.None
            : p.Name switch
            {
                "bankAccountId" => bankAccountId,
                "from" => from?.ToString("yyyy-MM-dd"),
                "to" => to?.ToString("yyyy-MM-dd"),
                _ => throw new InvalidOperationException($"Parametro inesperado {p.Name}")
            }).ToArray();
        var result = await (Task<IActionResult>)method.Invoke(controller, args)!;
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void Add(TestDb t, BankAccount? account, int count, TransactionType type, decimal amount, string name,
        DateTime first, Category? category = null)
    {
        for (var i = 0; i < count; i++)
            t.Db.Transactions.Add(new Transaction
            {
                UserId = t.User.Id, BankAccountId = account?.Id, CategoryId = category?.Id, Name = $"{name}{(count > 1 ? " " + i : "")}",
                Type = type, Status = TransactionStatus.Paid, Amount = amount,
                DueDate = DateTime.SpecifyKind(first.AddMinutes(i), DateTimeKind.Utc)
            });
        t.Db.SaveChanges();
    }

    private static JsonElement Month(JsonElement root, int year, int month) =>
        root.GetProperty("months").EnumerateArray().Single(m => m.GetProperty("year").GetInt32() == year && m.GetProperty("month").GetInt32() == month);

    private static TestDb Big(out BankAccount account, out BankAccount other)
    {
        var t = new TestDb("Igor Luis Lima");
        account = t.AddAccount("A");
        other = t.AddAccount("B");
        Add(t, account, 1000, TransactionType.Expense, 1.10m, "Compra", Jan);
        Add(t, account, 1500, TransactionType.Expense, 1.10m, "Compra", Mar);
        Add(t, account, 3, TransactionType.Income, 1000m, "Salario", new DateTime(2025, 1, 10));
        Add(t, account, 1, TransactionType.Income, 250.50m, "Venda", new DateTime(2025, 3, 5));
        Add(t, account, 1, TransactionType.Expense, 900m, "No limite superior", To);
        Add(t, account, 1, TransactionType.Expense, 800m, "Antes do inicio", From.AddSeconds(-1));
        Add(t, account, 1, TransactionType.Expense, 5000m, "Transferência enviada|IGOR L LIMA", new DateTime(2025, 1, 15));
        Add(t, account, 1, TransactionType.Income, 5000m, "Transferência recebida|Igor Luis Lima", new DateTime(2025, 1, 15));
        Add(t, other, 1, TransactionType.Expense, 77m, "Outra conta", new DateTime(2025, 2, 10));
        var stranger = new User { Name = "Outro", Email = "o@x.com", DocumentHash = "h2", PasswordHash = "x" };
        t.Db.Users.Add(stranger);
        t.Db.Transactions.Add(new Transaction
        {
            UserId = stranger.Id, BankAccountId = account.Id, Name = "Alheia", Type = TransactionType.Expense,
            Status = TransactionStatus.Paid, Amount = 999m, DueDate = DateTime.SpecifyKind(new DateTime(2025, 2, 1), DateTimeKind.Utc)
        });
        t.Db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task Totais_da_conta_A_com_mais_de_2000_lancamentos_2750_de_despesa_e_3250_50_de_receita()
    {
        using var t = Big(out var a, out _);

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(2750.00m, r.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(3250.50m, r.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(2504, r.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Meses_do_periodo_incluem_fevereiro_zerado_e_somam_os_valores_de_cada_mes()
    {
        using var t = Big(out var a, out _);

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(3, r.GetProperty("months").GetArrayLength());
        var jan = Month(r, 2025, 1);
        Assert.Equal(1100.00m, jan.GetProperty("expense").GetDecimal());
        Assert.Equal(3000.00m, jan.GetProperty("income").GetDecimal());
        Assert.Equal(1003, jan.GetProperty("count").GetInt32());
        var feb = Month(r, 2025, 2);
        Assert.Equal(0m, feb.GetProperty("expense").GetDecimal());
        Assert.Equal(0m, feb.GetProperty("income").GetDecimal());
        Assert.Equal(0, feb.GetProperty("count").GetInt32());
        var mar = Month(r, 2025, 3);
        Assert.Equal(1650.00m, mar.GetProperty("expense").GetDecimal());
        Assert.Equal(250.50m, mar.GetProperty("income").GetDecimal());
        Assert.Equal(1501, mar.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task From_e_inclusivo_e_to_e_exclusivo()
    {
        using var t = Big(out var a, out _);

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(2750.00m, r.GetProperty("totalExpense").GetDecimal());
        Assert.DoesNotContain(r.GetProperty("months").EnumerateArray(), m => m.GetProperty("month").GetInt32() == 4);
    }

    [Fact]
    public async Task Periodo_que_comeca_exatamente_no_instante_de_um_lancamento_o_inclui()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        t.AddTransaction(a, TransactionType.Expense, 10m, new DateTime(2025, 5, 1));
        t.AddTransaction(a, TransactionType.Expense, 20m, new DateTime(2025, 6, 1));

        var r = await Aggregates(t, a.Id, new DateTime(2025, 5, 1), new DateTime(2025, 6, 1));

        Assert.Equal(10m, r.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(1, r.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Transferencias_proprias_ficam_fora_dos_totais_e_dos_meses()
    {
        using var t = Big(out var a, out _);

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(1100.00m, Month(r, 2025, 1).GetProperty("expense").GetDecimal());
        Assert.Equal(3000.00m, Month(r, 2025, 1).GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task Sem_filtro_de_conta_soma_todas_as_contas_do_usuario_e_nao_as_de_outro_usuario()
    {
        using var t = Big(out _, out _);

        var r = await Aggregates(t, null, From, To);

        Assert.Equal(2827.00m, r.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(77.00m, Month(r, 2025, 2).GetProperty("expense").GetDecimal());
    }

    [Fact]
    public async Task Filtro_pela_conta_B_traz_so_os_77_de_fevereiro()
    {
        using var t = Big(out _, out var b);

        var r = await Aggregates(t, b.Id, From, To);

        Assert.Equal(77.00m, r.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(0m, r.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(1, r.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Categorias_somam_valor_e_contam_lancamentos_por_tipo()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        var mercado = t.AddCategory("Mercado");
        var salario = t.AddCategory("Salario");
        var day = new DateTime(2025, 2, 10);
        t.AddTransaction(a, TransactionType.Expense, 10.00m, day, "a", category: mercado);
        t.AddTransaction(a, TransactionType.Expense, 20.00m, day, "b", category: mercado);
        t.AddTransaction(a, TransactionType.Expense, 5.50m, day, "c", category: mercado);
        t.AddTransaction(a, TransactionType.Income, 3000m, day, "d", category: salario);
        t.AddTransaction(a, TransactionType.Income, 100m, day, "e", category: salario);

        var r = await Aggregates(t, a.Id, From, To);

        var categories = r.GetProperty("categories").EnumerateArray().ToList();
        var m = categories.Single(c => c.GetProperty("categoryName").GetString() == "Mercado");
        Assert.Equal(35.50m, m.GetProperty("amount").GetDecimal());
        Assert.Equal(3, m.GetProperty("count").GetInt32());
        Assert.Equal("expense", m.GetProperty("type").ToString().ToLowerInvariant());
        var s = categories.Single(c => c.GetProperty("categoryName").GetString() == "Salario");
        Assert.Equal(3100m, s.GetProperty("amount").GetDecimal());
        Assert.Equal(2, s.GetProperty("count").GetInt32());
        Assert.Equal("income", s.GetProperty("type").ToString().ToLowerInvariant());
    }

    private static List<JsonElement> Establishments(JsonElement r) => r.GetProperty("establishments").EnumerateArray().ToList();

    private static JsonElement Est(JsonElement r, string key, string type = "expense") =>
        Establishments(r).Single(e => e.GetProperty("key").GetString() == key
            && string.Equals(e.GetProperty("type").ToString(), type, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Estabelecimentos_agrupam_parcelas_com_sufixo_dd_mm_como_o_frontend()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        var day = new DateTime(2025, 2, 10);
        t.AddTransaction(a, TransactionType.Expense, 100m, day, "MERCADOLIVRE*OTHPR01/12");
        t.AddTransaction(a, TransactionType.Expense, 100m, day, "MERCADOLIVRE*OTHPR02/12");
        t.AddTransaction(a, TransactionType.Expense, 100.50m, day, "MERCADOLIVRE*OTHPR03/12");
        t.AddTransaction(a, TransactionType.Expense, 20m, day, "Padaria Central");
        t.AddTransaction(a, TransactionType.Expense, 15.50m, day, "Padaria Central");

        var r = await Aggregates(t, a.Id, From, To);

        var ml = Est(r, "MERCADOLIVRE*OTHPR");
        Assert.Equal(300.50m, ml.GetProperty("amount").GetDecimal());
        Assert.Equal(3, ml.GetProperty("count").GetInt32());
        var pad = Est(r, "Padaria Central");
        Assert.Equal(35.50m, pad.GetProperty("amount").GetDecimal());
        Assert.Equal(2, pad.GetProperty("count").GetInt32());
        Assert.Equal(2, Establishments(r).Count);
    }

    [Fact]
    public async Task Sufixo_com_espaco_e_trim_netflix_01_03_e_netflix_puro_viram_a_mesma_chave()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        var day = new DateTime(2025, 2, 10);
        t.AddTransaction(a, TransactionType.Expense, 39.90m, day, "Netflix 01/03");
        t.AddTransaction(a, TransactionType.Expense, 39.90m, day, "Netflix 02/03");
        t.AddTransaction(a, TransactionType.Expense, 39.90m, day, "Netflix");

        var r = await Aggregates(t, a.Id, From, To);

        var netflix = Est(r, "Netflix");
        Assert.Equal(119.70m, netflix.GetProperty("amount").GetDecimal());
        Assert.Equal(3, netflix.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Sufixo_so_vale_com_dois_digitos_dos_dois_lados_e_no_fim_do_nome()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        var day = new DateTime(2025, 2, 10);
        t.AddTransaction(a, TransactionType.Expense, 10m, day, "Loja 5/10");
        t.AddTransaction(a, TransactionType.Expense, 20m, day, "Loja 10/12 Centro");
        t.AddTransaction(a, TransactionType.Expense, 30m, day, "Loja 10/12");

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(10m, Est(r, "Loja 5/10").GetProperty("amount").GetDecimal());
        Assert.Equal(20m, Est(r, "Loja 10/12 Centro").GetProperty("amount").GetDecimal());
        Assert.Equal(30m, Est(r, "Loja").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Chave_diferencia_maiusculas_como_o_frontend_e_separa_por_tipo()
    {
        using var t = new TestDb();
        var a = t.AddAccount();
        var day = new DateTime(2025, 2, 10);
        t.AddTransaction(a, TransactionType.Expense, 10m, day, "Pix Fulano");
        t.AddTransaction(a, TransactionType.Expense, 5m, day, "PIX FULANO");
        t.AddTransaction(a, TransactionType.Income, 7m, day, "Pix Fulano");

        var r = await Aggregates(t, a.Id, From, To);

        Assert.Equal(10m, Est(r, "Pix Fulano").GetProperty("amount").GetDecimal());
        Assert.Equal(5m, Est(r, "PIX FULANO").GetProperty("amount").GetDecimal());
        Assert.Equal(7m, Est(r, "Pix Fulano", "income").GetProperty("amount").GetDecimal());
    }
}
