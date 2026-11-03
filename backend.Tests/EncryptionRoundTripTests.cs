using Microsoft.Extensions.Configuration;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

public class EncryptionRoundTripTests
{
    public static TheoryData<decimal> Valores => new()
    {
        0m, 0.01m, -0.01m, 1m, -1m, 99.99m, 100.00m, 1234.56m, -1234.56m,
        999_999_999.99m, -999_999_999.99m, 1_000_000_000_000.00m, 79228162514264337593543950335m
    };

    [Theory]
    [MemberData(nameof(Valores))]
    public void Decimal_cifrado_e_decifrado_volta_identico(decimal valor)
    {
        var service = TestEncryption.Create();

        Assert.Equal(valor, service.DecryptDecimal(service.EncryptDecimal(valor)));
    }

    [Fact]
    public void Tres_casas_decimais_sao_arredondadas_para_duas_na_persistencia_10_005_vira_10_01()
    {
        var service = TestEncryption.Create();

        Assert.Equal(10.01m, service.DecryptDecimal(service.EncryptDecimal(10.005m)));
        Assert.Equal(2.00m, service.DecryptDecimal(service.EncryptDecimal(1.999m)));
        Assert.Equal(-10.01m, service.DecryptDecimal(service.EncryptDecimal(-10.005m)));
    }

    [Fact]
    public void Cifrar_o_mesmo_valor_duas_vezes_gera_textos_diferentes_e_ambos_decifram()
    {
        var service = TestEncryption.Create();

        var a = service.EncryptDecimal(100m);
        var b = service.EncryptDecimal(100m);

        Assert.NotEqual(a, b);
        Assert.Equal(100m, service.DecryptDecimal(a));
        Assert.Equal(100m, service.DecryptDecimal(b));
    }

    [Fact]
    public void Texto_com_acentos_e_emoji_volta_identico()
    {
        var service = TestEncryption.Create();
        const string nome = "Pão de Açúcar - Ção ✓";

        Assert.Equal(nome, service.Decrypt(service.Encrypt(nome)));
    }

    [Fact]
    public void Texto_cifrado_adulterado_nao_decifra()
    {
        var service = TestEncryption.Create();
        var cipher = service.Encrypt("100.00");
        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0x01;

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => service.Decrypt(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Outra_chave_nao_decifra_o_valor()
    {
        var a = TestEncryption.Create();
        var b = TestEncryption.Create();

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => b.DecryptDecimal(a.EncryptDecimal(10m)));
    }

    [Fact]
    public void Valor_gravado_com_chave_legada_continua_legivel_apos_ativar_chave_versionada()
    {
        var legacySecret = TestEncryption.RandomSecret();
        var legacy = new EncryptionService(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = legacySecret }).Build());
        var old = legacy.EncryptDecimal(1234.56m);

        var rotated = new EncryptionService(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = legacySecret,
                ["Encryption:Keys:k1"] = TestEncryption.RandomSecret(),
                ["Encryption:ActiveKeyId"] = "k1"
            }).Build());

        var fresh = rotated.EncryptDecimal(-0.01m);
        Assert.StartsWith("cfk:k1:", fresh);
        Assert.Equal(1234.56m, rotated.DecryptDecimal(old));
        Assert.Equal(-0.01m, rotated.DecryptDecimal(fresh));
    }

    [Fact]
    public async Task Valores_de_transacao_gravados_no_banco_voltam_identicos_em_outro_contexto()
    {
        using var t = new TestDb();
        var account = t.AddAccount(balance: -1234.56m);
        decimal[] amounts = [0m, 0.01m, -0.01m, 99.99m, 999_999_999.99m];
        foreach (var amount in amounts)
            t.AddTransaction(account, TransactionType.Expense, amount, new DateTime(2025, 1, 1), $"v{amount}");

        await using var other = t.NewContext();
        var read = await other.Transactions.AsNoTracking().ToListAsync();

        foreach (var amount in amounts)
            Assert.Equal(amount, read.Single(x => x.Name == $"v{amount}").Amount);
        Assert.Equal(-1234.56m, (await other.BankAccounts.AsNoTracking().SingleAsync()).Balance);
    }

    [Fact]
    public async Task Colunas_do_banco_nao_guardam_valor_nem_nome_em_texto_puro()
    {
        using var t = new TestDb();
        var account = t.AddAccount(balance: 4321.09m);
        t.AddTransaction(account, TransactionType.Expense, 123.45m, new DateTime(2025, 1, 1), "Padaria Central");

        var raw = new List<string>();
        await using var command = t.Db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT AmountEncrypted, NameEncrypted FROM Transactions";
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) { raw.Add(reader.GetString(0)); raw.Add(reader.GetString(1)); }

        Assert.DoesNotContain("123.45", raw[0]);
        Assert.DoesNotContain("Padaria", raw[1]);
        Assert.NotEqual(raw[0], raw[1]);
    }

    [Fact]
    public async Task Campos_nulos_de_cartao_continuam_nulos_apos_ida_e_volta()
    {
        using var t = new TestDb();
        t.AddAccount("Cartao", type: "CREDIT_CARD", creditLimit: 0m, usedLimit: null);

        await using var other = t.NewContext();
        var read = await other.BankAccounts.AsNoTracking().SingleAsync();

        Assert.Equal(0m, read.CreditLimit);
        Assert.Null(read.UsedLimit);
    }

    [Fact]
    public async Task Parcelamento_com_centavos_volta_identico()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000.00m, 99.99m);
        t.AddInstallment(card, null, 33.33m, 1, 3, new DateTime(2099, 1, 10), totalAmount: 100.00m);

        await using var other = t.NewContext();
        var read = await other.Installments.AsNoTracking().SingleAsync();

        Assert.Equal(33.33m, read.InstallmentAmount);
        Assert.Equal(100.00m, read.TotalAmount);
        Assert.Equal("Compra", read.Description);
    }
}
